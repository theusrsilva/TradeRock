# Fontes embutidas

O app não consulta a internet (D17), então as fontes vão no build.

- **Nunito**, **Pixelify Sans** e **JetBrains Mono**: pacotes `@fontsource/*` (SIL OFL 1.1).
- **material-symbols-rounded.woff2**: ícones Material Symbols Rounded (Google, Apache-2.0), tirados do
  pacote npm `material-symbols` 0.47.6 e fixados em peso 400, grade 0 e tamanho óptico 24 (o eixo FILL
  ficou variável). O arquivo original tem 5,4 MB; este tem cerca de 540 KB. Para gerar de novo:

  ```sh
  pip install fonttools brotli
  python -c "from fontTools.ttLib import TTFont; from fontTools.varLib import instancer
  f = TTFont('node_modules/material-symbols/material-symbols-rounded.woff2')
  i = instancer.instantiateVariableFont(f, {'wght': 400, 'FILL': (0, 1), 'GRAD': 0, 'opsz': 24})
  i.flavor = 'woff2'; i.save('src/assets/fonts/material-symbols-rounded.woff2')"
  ```
