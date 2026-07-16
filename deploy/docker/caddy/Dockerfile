# Fase X1 (completitud del plan v2) — build custom de Caddy con el plugin de
# Cloudflare DNS. Necesario para emitir un certificado WILDCARD
# (*.taxprocore.com): Let's Encrypt solo emite wildcard via desafio DNS-01, y
# el desafio DNS-01 exige que Caddy pueda crear el registro TXT de validacion
# el mismo — de ahi el plugin caddy-dns/cloudflare (usa el mismo
# CLOUDFLARE_API_TOKEN que ya declara docker-compose.yml para el servicio
# caddy, con permiso Zone:DNS:Edit sobre la zona).
#
# El Caddyfile anterior (un solo site block sobre TAXVISION_DOMAIN, TLS via
# HTTP-01 automatico) seguia funcionando para un unico dominio, pero HTTP-01
# no puede emitir certs para un Host que Caddy no conoce de antemano — es
# decir, nunca iba a poder servir TLS para subdominios de tenant dinamicos
# (oficina1.taxprocore.com, oficina2.taxprocore.com, ...) sin este cambio.
FROM caddy:2-builder-alpine AS builder

RUN xcaddy build --with github.com/caddy-dns/cloudflare

FROM caddy:2-alpine

COPY --from=builder /usr/bin/caddy /usr/bin/caddy
