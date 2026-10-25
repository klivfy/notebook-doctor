#!/usr/bin/env python3
"""Traduções do Notebook Doctor (en.json e es.json).

  python3 scripts/traducoes.py faltando            lista as chaves do código sem tradução em inglês ou espanhol
  python3 scripts/traducoes.py juntar novas.json   junta {"texto em português": ["inglês", "espanhol"]} aos dois arquivos

A chave é o texto em português que vai em Loc.T/Loc.F, Pt.N, ShowSnack e Simulate. Os arquivos ficam ordenados pela
chave em minúsculas, com recuo de 1 espaço, como o LocTests espera. Uma chave que já existe não é trocada: o script
avisa e mantém a tradução atual. O LocTests continua sendo a conferência final (marcadores {0} e teclas de acesso).
"""
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANGS = ["en", "es"]
LIT = r'"((?:[^"\\]|\\.)*)"'


def path(lang):
    return os.path.join(ROOT, "src", "NotebookDoctor.Core", "Idiomas", f"{lang}.json")


def load(lang):
    with open(path(lang), encoding="utf-8") as f:
        return json.load(f)


def save(lang, data):
    ordered = dict(sorted(data.items(), key=lambda kv: kv[0].lower()))
    with open(path(lang), "w", encoding="utf-8") as f:
        f.write(json.dumps(ordered, ensure_ascii=False, indent=1))


def unescape(s):
    return json.loads(f'"{s}"') if "\\" in s else s


def code_keys():
    keys = {}
    files = [f for f in glob.glob(os.path.join(ROOT, "**", "*.cs"), recursive=True)
             if os.sep + "obj" + os.sep not in f and os.sep + "bin" + os.sep not in f and "Tests" not in f]
    for f in files:
        with open(f, encoding="utf-8") as h:
            s = h.read()
        rel = os.path.relpath(f, ROOT)
        for m in re.finditer(r"Loc\.[TF]\(\s*" + LIT, s):
            keys.setdefault(unescape(m.group(1)), rel)
        for m in re.finditer(r'Loc\.[TF]\([^;"]*?\?\s*' + LIT + r"\s*:\s*" + LIT, s):
            keys.setdefault(unescape(m.group(1)), rel)
            keys.setdefault(unescape(m.group(2)), rel)
        for m in re.finditer(r"Pt\.N\([^,]+,\s*" + LIT + r"\s*,\s*" + LIT, s):
            keys.setdefault(unescape(m.group(1)), rel)
            keys.setdefault(unescape(m.group(2)), rel)
        for m in re.finditer(r"(?:ShowSnack|Simulate)\(([^;]*)\);", s):
            # Literais comparados (t.Id == "pagefile") são identificadores, não texto da tela
            for lm in re.finditer(r"(?<![$\w])(?<![=!]=\s)" + LIT, m.group(1)):
                k = unescape(lm.group(1))
                if re.search(r"[A-Za-zÀ-ú]{3}", k):
                    keys.setdefault(k, rel)
    return keys


def missing():
    have = [set(load(lang)) for lang in LANGS]
    found = 0
    for k, f in sorted(code_keys().items(), key=lambda kv: kv[1]):
        if not re.search(r"[A-Za-zÀ-ú]{2}", k):
            continue
        langs = [lang for lang, h in zip(LANGS, have) if k not in h]
        if langs:
            found += 1
            print(f"{f}\t{','.join(langs)}\t{k}")
    print(f"{found} faltando", file=sys.stderr)
    return found


def merge(file):
    with open(file, encoding="utf-8") as f:
        new = json.load(f)
    for i, lang in enumerate(LANGS):
        data = load(lang)
        added = 0
        for k, v in new.items():
            if k in data:
                if data[k] != v[i]:
                    print(f"[{lang}] já existe, mantido: {k!r} = {data[k]!r}")
                continue
            data[k] = v[i]
            added += 1
        save(lang, data)
        print(f"{lang}: {added} novas, {len(data)} no total")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "faltando":
        sys.exit(1 if missing() else 0)
    if len(sys.argv) == 3 and sys.argv[1] == "juntar":
        merge(sys.argv[2])
        sys.exit(0)
    print(__doc__)
    sys.exit(2)
