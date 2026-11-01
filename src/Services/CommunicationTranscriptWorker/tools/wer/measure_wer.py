"""
Mide la exactitud del transcript contra referencias escritas a mano (INV-T01 §7).

Herramienta offline de desarrollo, no corre en producción ni entra en la imagen del worker.

Uso:
    python measure_wer.py --manifest corpus/manifest.json [--glossary glosario.txt]
                          [--out run.json] [--compare linea_base.json]

Manifest (JSON):
    [{"id": "reunion-1-ana", "reference": "ref/reunion-1-ana.txt",
      "hypothesis": "hyp/reunion-1.json", "speakerUserId": "<opcional>"}]
    Las rutas son relativas al manifest.

Referencia: un segmento por línea, con tiempos y etiqueta de idioma (EN, ES o MIX):
    00:00:01.0-00:00:04.5 [ES] Buenos días, ¿ya mandó el W-2?
    Las líneas vacías y las que empiezan con # se ignoran. Se escribe lo que se dijo, en el idioma en
    que se dijo, sin traducir.

Hipótesis: el JSON v2 que sube el worker (segments[].startMs/endMs/text, filtrable por speakerUserId)
o la salida -oj de whisper-cli (transcription[].offsets.from/to + text).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from dataclasses import dataclass, field
from pathlib import Path

try:
    import jiwer
except ImportError:  # pragma: no cover - mensaje para quien lo corre sin instalar
    sys.exit("Falta jiwer: pip install -r requirements.txt")

LANGS = ("EN", "ES", "MIX")
REF_LINE = re.compile(
    r"^\s*(?P<start>[\d:.]+)\s*-\s*(?P<end>[\d:.]+)\s*\[(?P<lang>EN|ES|MIX)\]\s*(?P<text>.*)$",
    re.IGNORECASE,
)

# Palabras funcionales frecuentes: bastan para decir si un texto salió en inglés o en español.
EN_WORDS = set(
    "the a an and or but of to in on at for with from by is are was were be been it this that these "
    "those i you he she we they my your our their me him her us them not do does did have has had will "
    "would can could should what which who when where why how if so just yes no please thank thanks".split()
)
ES_WORDS = set(
    "el la los las un una unos unas y o pero de del a al en con por para es son era fue ser estar está "
    "están esto eso este esta estos estas yo tú usted él ella nosotros ellos mi tu su nuestro me te le "
    "nos les no sí que qué cual cuál quien quién cuando cuándo donde dónde porque cómo como si ya muy "
    "gracias favor también pues bueno".split()
)


@dataclass
class Segment:
    start_ms: int
    end_ms: int
    text: str
    lang: str | None = None


@dataclass
class Bucket:
    refs: list[str] = field(default_factory=list)
    hyps: list[str] = field(default_factory=list)
    segments: int = 0
    wrong_language: int = 0


def parse_time(value: str) -> int:
    """hh:mm:ss(.f), mm:ss(.f) o segundos → ms."""
    seconds = 0.0
    for part in value.split(":"):
        seconds = seconds * 60 + float(part)
    return round(seconds * 1000)


def normalize(text: str, keep_accents: bool = True) -> str:
    """Minúsculas, sin puntuación, espacios colapsados (como BasicTextNormalizer de Whisper)."""
    text = unicodedata.normalize("NFC", text.lower())
    if not keep_accents:
        text = "".join(c for c in unicodedata.normalize("NFD", text) if unicodedata.category(c) != "Mn")
    text = "".join(" " if unicodedata.category(c).startswith(("P", "S")) else c for c in text)
    return " ".join(text.split())


def text_language(text: str) -> str | None:
    """EN, ES o None (no se puede decir) según las palabras funcionales; ñ y tildes cuentan para ES."""
    words = normalize(text).split()
    en = sum(w in EN_WORDS for w in words)
    es = sum(w in ES_WORDS for w in words) + sum(any(c in "ñáéíóú¿¡" for c in w) for w in words)
    if en + es < 2 or en == es:
        return None
    return "EN" if en > es else "ES"


def load_reference(path: Path) -> list[Segment]:
    segments = []
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        match = REF_LINE.match(line)
        if not match:
            raise ValueError(f"{path}:{number}: se esperaba 'inicio-fin [EN|ES|MIX] texto'")
        segments.append(
            Segment(parse_time(match["start"]), parse_time(match["end"]), match["text"].strip(), match["lang"].upper())
        )
    return segments


def load_hypothesis(path: Path, speaker_user_id: str | None) -> list[Segment]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if "segments" in data:  # JSON v2 del worker
        return [
            Segment(int(s["startMs"]), int(s["endMs"]), s["text"])
            for s in data["segments"]
            if not speaker_user_id or s.get("speakerUserId") == speaker_user_id
        ]
    if "transcription" in data:  # whisper-cli -oj
        return [
            Segment(int(s["offsets"]["from"]), int(s["offsets"]["to"]), s["text"].strip())
            for s in data["transcription"]
        ]
    raise ValueError(f"{path}: formato de hipótesis desconocido (ni JSON v2 ni whisper-cli -oj)")


def overlap(a: Segment, b: Segment) -> int:
    return max(0, min(a.end_ms, b.end_ms) - max(a.start_ms, b.start_ms))


def assign(refs: list[Segment], hyps: list[Segment]) -> tuple[list[list[str]], list[str]]:
    """Cada segmento de salida va al de referencia con el que más se solapa; sin solape = texto en silencio."""
    per_ref: list[list[str]] = [[] for _ in refs]
    in_silence: list[str] = []
    for hyp in hyps:
        best = max(range(len(refs)), key=lambda i: overlap(refs[i], hyp), default=None)
        if best is None or overlap(refs[best], hyp) == 0:
            in_silence.append(hyp.text)
        else:
            per_ref[best].append(hyp.text)
    return per_ref, in_silence


def scores(refs: list[str], hyps: list[str], keep_accents: bool = True) -> dict[str, float] | None:
    pairs = [(normalize(r, keep_accents), normalize(h, keep_accents)) for r, h in zip(refs, hyps)]
    pairs = [(r, h) for r, h in pairs if r]
    if not pairs:
        return None
    ref, hyp = [r for r, _ in pairs], [h for _, h in pairs]
    return {"wer": jiwer.wer(ref, hyp), "cer": jiwer.cer(ref, hyp), "words": sum(len(r.split()) for r in ref)}


def glossary_recall(terms: list[str], refs: list[str], hyps: list[str]) -> dict[str, float] | None:
    """De los términos del glosario que se dijeron, cuántos salen escritos igual."""
    ref_text, hyp_text = f" {normalize(' '.join(refs))} ", f" {normalize(' '.join(hyps))} "
    said = [t for t in terms if f" {normalize(t)} " in ref_text]
    if not said:
        return None
    found = sum(f" {normalize(t)} " in hyp_text for t in said)
    return {"recall": found / len(said), "said": len(said), "found": found}


def measure(manifest: Path, glossary: list[str]) -> dict:
    base = manifest.parent
    buckets = {lang: Bucket() for lang in LANGS}
    silence_words = 0
    all_refs: list[str] = []
    all_hyps: list[str] = []
    items = json.loads(manifest.read_text(encoding="utf-8"))
    for item in items:
        refs = load_reference(base / item["reference"])
        hyps = load_hypothesis(base / item["hypothesis"], item.get("speakerUserId"))
        per_ref, in_silence = assign(refs, hyps)
        silence_words += sum(len(normalize(t).split()) for t in in_silence)
        for ref, texts in zip(refs, per_ref):
            hyp = " ".join(texts)
            bucket = buckets[ref.lang or "MIX"]
            bucket.refs.append(ref.text)
            bucket.hyps.append(hyp)
            all_refs.append(ref.text)
            all_hyps.append(hyp)
            # Idioma equivocado o traducido: solo se puede juzgar en segmentos de un solo idioma.
            if ref.lang in ("EN", "ES"):
                bucket.segments += 1
                got = text_language(hyp)
                if got and got != ref.lang:
                    bucket.wrong_language += 1

    single = [buckets["EN"], buckets["ES"]]
    judged = sum(b.segments for b in single)
    return {
        "items": len(items),
        "total": scores(all_refs, all_hyps),
        "totalNoAccents": scores(all_refs, all_hyps, keep_accents=False),
        "byLanguage": {lang: scores(b.refs, b.hyps) for lang, b in buckets.items()},
        "wrongLanguageRate": (sum(b.wrong_language for b in single) / judged) if judged else None,
        "wrongLanguageByLanguage": {
            lang: (buckets[lang].wrong_language / buckets[lang].segments) if buckets[lang].segments else None
            for lang in ("EN", "ES")
        },
        "wordsInSilence": silence_words,
        "glossary": glossary_recall(glossary, all_refs, all_hyps) if glossary else None,
    }


def verdict(current: dict, baseline: dict) -> list[str]:
    """Criterio de adopción de INV-T01 §7.6."""

    def rel(new: float, old: float) -> float:
        return (new - old) / old if old else 0.0

    lines, adopt = [], True
    total = rel(current["total"]["wer"], baseline["total"]["wer"])
    lines.append(f"WER total: {baseline['total']['wer']:.2%} → {current['total']['wer']:.2%} ({total:+.1%} relativo)")
    glossary_gain = 0.0
    if current.get("glossary") and baseline.get("glossary"):
        glossary_gain = current["glossary"]["recall"] - baseline["glossary"]["recall"]
        lines.append(f"Recall del glosario: {glossary_gain * 100:+.1f} puntos")
    if total > -0.05 and glossary_gain < 0.10:
        adopt = False
        lines.append("  ✗ no baja el WER total un 5 % relativo ni sube el glosario 10 puntos")
    for lang in LANGS:
        new, old = current["byLanguage"].get(lang), baseline["byLanguage"].get(lang)
        if new and old:
            change = rel(new["wer"], old["wer"])
            mark = "✗" if change > 0.02 else " "
            lines.append(f"  {mark} {lang}: {old['wer']:.2%} → {new['wer']:.2%} ({change:+.1%})")
            if change > 0.02:
                adopt = False
    if (current["wrongLanguageRate"] or 0) > (baseline["wrongLanguageRate"] or 0):
        adopt = False
        lines.append("  ✗ sube la tasa de idioma equivocado o traducido")
    if current["wordsInSilence"] > baseline["wordsInSilence"]:
        adopt = False
        lines.append("  ✗ suben las palabras inventadas en silencio")
    lines.append("ADOPTAR" if adopt else "NO ADOPTAR")
    return lines


def report(result: dict) -> list[str]:
    def fmt(s: dict | None) -> str:
        return "—" if not s else f"WER {s['wer']:.2%}  CER {s['cer']:.2%}  ({s['words']} palabras)"

    def pct(v: float | None) -> str:
        return "—" if v is None else f"{v:.1%}"

    lines = [
        f"Muestras: {result['items']}",
        f"Total:            {fmt(result['total'])}",
        f"Total sin tildes: {fmt(result['totalNoAccents'])}",
    ]
    for lang in LANGS:
        lines.append(f"  {lang:<3}             {fmt(result['byLanguage'][lang])}")
    by_lang = result["wrongLanguageByLanguage"]
    lines.append(
        f"Idioma equivocado/traducido: {pct(result['wrongLanguageRate'])} "
        f"(EN {pct(by_lang['EN'])}, ES {pct(by_lang['ES'])})"
    )
    lines.append(f"Palabras en silencio (alucinación): {result['wordsInSilence']}")
    if result["glossary"]:
        g = result["glossary"]
        lines.append(f"Glosario: {g['recall']:.0%} ({g['found']}/{g['said']} términos dichos)")
    return lines


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="WER/CER por idioma del transcript (INV-T01 §7).")
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--glossary", type=Path, help="un término por línea (nombres, siglas, anglicismos)")
    parser.add_argument("--out", type=Path, help="guarda el resultado en JSON para compararlo después")
    parser.add_argument("--compare", type=Path, help="JSON de una corrida anterior (línea base)")
    args = parser.parse_args(argv)
    # La consola de Windows no es UTF-8 por defecto (tildes, ✗, →).
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    glossary = []
    if args.glossary:
        glossary = [t.strip() for t in args.glossary.read_text(encoding="utf-8").splitlines() if t.strip()]
    result = measure(args.manifest, glossary)
    print("\n".join(report(result)))
    if args.out:
        args.out.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    if args.compare:
        print("\nContra la línea base:")
        print("\n".join(verdict(result, json.loads(args.compare.read_text(encoding="utf-8")))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
