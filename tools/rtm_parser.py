#!/usr/bin/env python3
"""
Validador do parser do formato .rtm (ReportBuilder / streaming DFM do Delphi).

Objetivo: provar que conseguimos ler o template (bandas, grupos, elementos,
propriedades, bloco binario da imagem) antes de escrever o parser em C#.

Uso:
    python3 rtm_parser.py ../samples/resumo-coletas-eua.rtm
"""
import sys
import re


class DfmObject:
    def __init__(self, name, type_):
        self.name = name          # ex.: ppHeaderBand1
        self.type = type_         # ex.: TppHeaderBand
        self.props = {}           # dict propriedade -> valor
        self.children = []        # objetos aninhados (DfmObject)

    def __repr__(self):
        return f"{self.name}: {self.type} ({len(self.props)} props, {len(self.children)} filhos)"


class DfmParser:
    """
    Parser recursivo simples do formato texto do Delphi (.dfm / .rtm).
    Trabalha linha a linha, mantendo um 'cursor' no indice da linha atual.
    """
    def __init__(self, text):
        # normaliza quebras de linha e separa em linhas
        self.lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
        self.i = 0

    def peek(self):
        return self.lines[self.i] if self.i < len(self.lines) else None

    def parse(self):
        # pula linhas em branco iniciais ate achar "object ..."
        while self.peek() is not None and self.peek().strip() == "":
            self.i += 1
        return self._parse_object()

    def _parse_object(self):
        line = self.peek().strip()
        # cabecalho com nome:  "object <Nome>: <Tipo>"
        m = re.match(r"^(object|inherited)\s+(\w+):\s+(\w+)", line)
        if m:
            obj = DfmObject(m.group(2), m.group(3))
        else:
            # cabecalho so com tipo (caso da raiz): "object TppReport"
            m = re.match(r"^(object|inherited)\s+(\w+)\s*$", line)
            if not m:
                raise ValueError(f"Esperava 'object [Nome:] Tipo', achei: {line!r}")
            obj = DfmObject(m.group(2), m.group(2))
        self.i += 1

        while True:
            raw = self.peek()
            if raw is None:
                raise ValueError("Fim de arquivo antes de 'end'")
            s = raw.strip()

            if s == "end":
                self.i += 1
                break
            if s == "":
                self.i += 1
                continue
            if s.startswith("object ") or s.startswith("inherited "):
                obj.children.append(self._parse_object())
                continue

            # senao: e uma propriedade  chave = valor
            key, value = self._parse_property()
            obj.props[key] = value

        return obj

    def _parse_property(self):
        raw = self.lines[self.i]
        s = raw.strip()
        if "=" not in s:
            raise ValueError(f"Propriedade sem '=': {s!r}")
        key, _, rest = s.partition("=")
        key = key.strip()
        rest = rest.strip()
        self.i += 1

        # DFM quebra valores longos para a linha seguinte: "Caption =" e o valor
        # comeca na proxima linha. Se nada vem depois do '=', puxa a proxima.
        if rest == "" and self.i < len(self.lines):
            rest = self.lines[self.i].strip()
            self.i += 1

        # bloco binario: valor comeca com '{' -> acumula hex ate achar '}'
        if rest.startswith("{"):
            return key, self._read_binary(rest)

        # colecao de itens: valor comeca com '<' -> le ate '>'
        if rest.startswith("<"):
            return key, self._read_collection(rest)

        # string (pode continuar em varias linhas com '+' no fim)
        if rest.startswith("'") or rest.startswith("#"):
            return key, self._read_string(rest)

        # valor simples numa linha (numero, identificador, set [..], bool)
        return key, self._parse_scalar(rest)

    def _read_binary(self, first):
        hexstr = first.lstrip("{").strip()
        while "}" not in hexstr:
            hexstr += self.peek().strip()
            self.i += 1
        hexstr = hexstr.replace("}", "").strip()
        hexstr = re.sub(r"\s+", "", hexstr)
        return bytes.fromhex(hexstr)

    def _read_collection(self, first):
        # junta tudo ate o '>' que fecha a colecao
        buf = first
        while ">" not in buf:
            buf += " " + self.peek().strip()
            self.i += 1
        return buf  # para validacao, basta guardar o texto cru

    def _read_string(self, first):
        # Delphi quebra strings longas com '+' no fim da linha.
        parts = [first]
        while parts[-1].rstrip().endswith("+"):
            parts[-1] = parts[-1].rstrip()[:-1]  # remove o '+'
            parts.append(self.peek().strip())
            self.i += 1
        return self._decode_delphi_string("".join(parts))

    @staticmethod
    def _decode_delphi_string(tok):
        """Decodifica 'texto''com aspas'#13#10'mais' -> string Python."""
        out = []
        i = 0
        while i < len(tok):
            c = tok[i]
            if c == "'":
                i += 1
                while i < len(tok):
                    if tok[i] == "'" and i + 1 < len(tok) and tok[i + 1] == "'":
                        out.append("'")
                        i += 2
                    elif tok[i] == "'":
                        i += 1
                        break
                    else:
                        out.append(tok[i])
                        i += 1
            elif c == "#":
                j = i + 1
                num = ""
                while j < len(tok) and tok[j].isdigit():
                    num += tok[j]
                    j += 1
                if num:
                    out.append(chr(int(num)))
                i = j
            else:
                i += 1
        return "".join(out)

    @staticmethod
    def _parse_scalar(rest):
        if rest == "True":
            return True
        if rest == "False":
            return False
        if rest.startswith("["):
            inner = rest.strip("[]").strip()
            return [x.strip() for x in inner.split(",")] if inner else []
        # numero?
        if re.match(r"^-?\d+$", rest):
            return int(rest)
        if re.match(r"^-?\d+\.\d+$", rest):
            return float(rest)
        # identificador/enum (poLandscape, clBlack, DEFAULT_CHARSET, ...)
        return rest


# ---- banda -> rotulo amigavel ---------------------------------------------
BAND_LABEL = {
    "TppHeaderBand": "Cabecalho (topo de cada pagina)",
    "TppDetailBand": "Detalhe (uma por linha do dataset)",
    "TppFooterBand": "Rodape",
    "TppGroupHeaderBand": "Cabecalho de grupo",
    "TppGroupFooterBand": "Rodape de grupo",
    "TppTitleBand": "Titulo",
    "TppSummaryBand": "Sumario",
}


def mm(v):
    """ReportBuilder guarda em milesimos de mm -> devolve em mm."""
    return round(v / 1000.0, 2) if isinstance(v, (int, float)) else v


def dump(report):
    print(f"\n=== REPORT: {report.type} ===")
    ps = report.props
    print(f"  Papel   : {ps.get('PrinterSetup.PaperName')} "
          f"{mm(ps.get('PrinterSetup.mmPaperWidth',0))} x "
          f"{mm(ps.get('PrinterSetup.mmPaperHeight',0))} mm  "
          f"({ps.get('PrinterSetup.Orientation')})")
    print(f"  Dados   : {ps.get('DataPipelineName')}")

    groups = [c for c in report.children if c.type == "TppGroup"]
    bands = [c for c in report.children if c.type.endswith("Band")]

    # grupos definidos direto no report (quebras)
    if groups:
        print("\n  GRUPOS (quebras):")
        for g in groups:
            print(f"    - {g.props.get('UserName', g.name)}: quebra em "
                  f"'{g.props.get('BreakName')}'")

    def dump_band(b, indent="  "):
        label = BAND_LABEL.get(b.type, b.type)
        print(f"{indent}BANDA {b.type} -> {label}  (altura {mm(b.props.get('mmHeight',0))} mm)")
        for el in b.children:
            _dump_element(el, indent + "    ")

    print("\n  BANDAS no corpo do report:")
    for b in bands:
        dump_band(b)

    # bandas dentro dos grupos
    for g in groups:
        for b in g.children:
            print(f"\n  BANDAS do grupo {g.props.get('UserName', g.name)}:")
            dump_band(b)


def _dump_element(el, indent):
    p = el.props
    pos = f"[x={mm(p.get('mmLeft',0))} y={mm(p.get('mmTop',0))} "\
          f"w={mm(p.get('mmWidth',0))} h={mm(p.get('mmHeight',0))}]"
    if el.type == "TppLabel":
        print(f"{indent}Label  {pos}  texto={p.get('Caption')!r} "
              f"fonte={p.get('Font.Name')}/{p.get('Font.Size')} "
              f"estilo={p.get('Font.Style')} align={p.get('TextAlignment','taLeft')}")
    elif el.type == "TppDBText":
        print(f"{indent}DBText {pos}  campo={p.get('DataField')!r} "
              f"estilo={p.get('Font.Style')}")
    elif el.type == "TppImage":
        data = p.get("Picture.Data", b"")
        jpeg = _extract_jpeg(data)
        ok = "JPEG ok" if jpeg else "nao reconhecido"
        print(f"{indent}Image  {pos}  blob={len(data)}B -> {ok} "
              f"({len(jpeg)}B de imagem)")
    elif el.type == "TppLine":
        print(f"{indent}Line   {pos}  peso={p.get('Weight')}")
    elif el.type == "TppShape":
        print(f"{indent}Shape  {pos}  cor={p.get('Brush.Color','clWhite')}")
    else:
        print(f"{indent}{el.type} {pos}")


def _extract_jpeg(data):
    """
    Picture.Data vem embrulhado pelo Delphi (TPicture):
    1 byte = tamanho do nome da classe, depois 'TJPEGImage', 4 bytes de
    tamanho, e entao os bytes JPEG (FFD8 ... FFD9). Extraimos de FFD8 a FFD9.
    """
    if not isinstance(data, (bytes, bytearray)):
        return b""
    start = data.find(b"\xff\xd8")
    end = data.rfind(b"\xff\xd9")
    if start != -1 and end != -1 and end > start:
        return bytes(data[start:end + 2])
    return b""


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "../samples/resumo-coletas-eua.rtm"
    with open(path, "r", encoding="latin-1") as f:
        text = f.read()
    report = DfmParser(text).parse()
    dump(report)
    print("\n>> Parser validado: a arvore foi reconstruida sem erros.\n")


if __name__ == "__main__":
    main()
