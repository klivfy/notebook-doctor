#!/usr/bin/env python3
"""Baixa as telas da CI (artefato "telas") e resume o smoke.txt.

  python3 scripts/telas.py [branch]          última execução da CI no branch (padrão: o branch atual)
  python3 scripts/telas.py --run 123456789   uma execução específica

Precisa do GitHub CLI (gh) autenticado. As imagens ficam em smoke-out/<commit>/ (fora do git, como bin/). O resumo
lista o que o app aponta no smoke.txt: tela que não abriu, ligação quebrada, texto sem tradução e texto com cara de
português na interface em inglês.
"""
import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# Linhas que o Smoke.cs escreve quando algo precisa de atenção (avisos de ligação e de tradução, tela que não abriu)
FLAGS = ("aviso", "ERRO", "FALHAS")


def gh(*args):
    return subprocess.run(["gh", *args], cwd=ROOT, check=True, capture_output=True, text=True).stdout


def latest_run(branch):
    runs = json.loads(gh("run", "list", "--branch", branch, "--workflow", "ci.yml", "--limit", "1",
                         "--json", "databaseId,headSha,status,conclusion"))
    if not runs:
        sys.exit(f"Nenhuma execução da CI no branch {branch}.")
    return runs[0]


def main():
    if len(sys.argv) == 3 and sys.argv[1] == "--run":
        run = json.loads(gh("run", "view", sys.argv[2], "--json", "databaseId,headSha,status,conclusion"))
    else:
        branch = sys.argv[1] if len(sys.argv) == 2 else subprocess.run(
            ["git", "rev-parse", "--abbrev-ref", "HEAD"], cwd=ROOT, check=True, capture_output=True, text=True).stdout.strip()
        run = latest_run(branch)
    print(f"Execução {run['databaseId']} · commit {run['headSha'][:7]} · {run['status']} {run.get('conclusion') or ''}")
    if run["status"] != "completed":
        sys.exit("A CI ainda está rodando: tente de novo quando terminar.")

    out = os.path.join(ROOT, "smoke-out", run["headSha"][:7])
    if not os.path.isdir(out):
        gh("run", "download", str(run["databaseId"]), "--name", "telas", "--dir", out)
    images = sorted(f for f in os.listdir(out) if f.lower().endswith(".png"))
    print(f"{len(images)} imagens em {os.path.relpath(out, ROOT)}")

    report = os.path.join(out, "smoke.txt")
    if not os.path.exists(report):
        sys.exit("O artefato não tem smoke.txt (o app não terminou a verificação).")
    with open(report, encoding="utf-8-sig") as f:
        lines = [line.rstrip() for line in f]
    flagged = [line for line in lines if line.startswith(FLAGS)]
    print(f"{len(flagged)} linhas para olhar no smoke.txt" if flagged else "smoke.txt sem apontamentos")
    for line in flagged:
        print("  " + line)


if __name__ == "__main__":
    main()
