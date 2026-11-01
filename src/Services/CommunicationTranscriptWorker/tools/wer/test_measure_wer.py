"""Pruebas de measure_wer.py sobre el corpus de muestra: python -m unittest test_measure_wer"""

import unittest
from pathlib import Path

import measure_wer as m

SAMPLE = Path(__file__).parent / "sample"


class MeasureWerTest(unittest.TestCase):
    def setUp(self):
        self.result = m.measure(SAMPLE / "manifest.json", ["W-2", "1099", "email"])

    def test_wer_total_y_por_idioma(self):
        # 24 palabras de referencia; 1 sustitución en MIX y una frase inglesa traducida (5 sust. + 1 borrado).
        self.assertEqual(self.result["total"]["words"], 24)
        self.assertAlmostEqual(self.result["total"]["wer"], 7 / 24)
        self.assertEqual(self.result["byLanguage"]["ES"]["wer"], 0)
        self.assertAlmostEqual(self.result["byLanguage"]["MIX"]["wer"], 1 / 6)
        self.assertAlmostEqual(self.result["byLanguage"]["EN"]["wer"], 6 / 11)

    def test_detecta_la_frase_traducida(self):
        self.assertAlmostEqual(self.result["wrongLanguageRate"], 1 / 3)
        self.assertEqual(self.result["wrongLanguageByLanguage"], {"EN": 0.5, "ES": 0.0})

    def test_cuenta_lo_inventado_en_silencio_y_el_glosario(self):
        self.assertEqual(self.result["wordsInSilence"], 3)
        self.assertEqual(self.result["glossary"], {"recall": 2 / 3, "said": 3, "found": 2})

    def test_normaliza_como_whisper_y_opcionalmente_sin_tildes(self):
        self.assertEqual(m.normalize("¿Ya mandó el W-2?"), "ya mandó el w 2")
        self.assertEqual(m.normalize("¿Ya mandó?", keep_accents=False), "ya mando")

    def test_criterio_de_adopcion(self):
        better = {**self.result, "total": {**self.result["total"], "wer": self.result["total"]["wer"] * 0.8}}
        self.assertEqual(m.verdict(better, self.result)[-1], "ADOPTAR")
        worse_es = {
            **better,
            "byLanguage": {**self.result["byLanguage"], "MIX": {**self.result["byLanguage"]["MIX"], "wer": 0.5}},
        }
        self.assertEqual(m.verdict(worse_es, self.result)[-1], "NO ADOPTAR")
        hallucinating = {**better, "wordsInSilence": 10}
        self.assertEqual(m.verdict(hallucinating, self.result)[-1], "NO ADOPTAR")

    def test_referencia_mal_escrita_dice_la_linea(self):
        bad = SAMPLE.parent / "bad_ref.tmp"
        bad.write_text("00:00:01-00:00:02 texto sin idioma\n", encoding="utf-8")
        try:
            with self.assertRaisesRegex(ValueError, ":1:"):
                m.load_reference(bad)
        finally:
            bad.unlink()


if __name__ == "__main__":
    unittest.main()
