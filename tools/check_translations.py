#!/usr/bin/env python3
"""Checks that every user-facing English template in ShivaAssist.Core has a translation in every
Localization/*.json file, with the same placeholders. Run from the repository root; exit code 1 on problems.

Templates are found where findings are built: Title/SuspectedCause/Recommendation literals, new Message(...),
Finding.Unknown(...) titles, network Problem(...) texts and the category/severity/reason names in ReportText.
"""
import glob
import json
import re
import sys

LIT=r'"((?:[^"\\]|\\.)*)"'
def templates(root):
    out=[]
    files=sorted(glob.glob(root+'/src/ShivaAssist.Core/**/*.cs',recursive=True)+glob.glob(root+'/src/ShivaAssist.App/**/*.cs',recursive=True)+glob.glob(root+'/src/ShivaAssist.Setup/**/*.cs',recursive=True))
    for f in files:
        s=open(f,encoding='utf-8').read()
        for m in re.finditer(r'new Message\(\s*'+LIT,s): out.append(m.group(1))
        for m in re.finditer(r'\bT\(\s*'+LIT,s): out.append(m.group(1))
        for m in re.finditer(r'Message Name => '+LIT,s): out.append(m.group(1))
        for m in re.finditer(r'Finding\.Unknown\("[^"]+",\s*[^,]+,\s*[^,]+,\s*'+LIT,s): out.append(m.group(1))
        for m in re.finditer(r'Problem\((.*?)\)\]',s,re.S):
            out+=re.findall(LIT,m.group(1))
        for line in s.splitlines():
            t=line.strip()

            if 'Title = ' in t or 'SuspectedCause = ' in t or 'Recommendation = new(' in t or re.match(r'(Name|Description) = "',t):
                seg=t
                # only literals belonging to Title/Cause/Recommendation on one-line findings
                for key in ['Title = ','SuspectedCause = ','Recommendation = new(','Name = ','Description = ']:
                    for m in re.finditer(re.escape(key)+r'([^,]*?\? )?'+LIT+r'(\s*:\s*'+LIT+')?',t):
                        out.append(m.group(2))
                        if m.group(4): out.append(m.group(4))
            elif re.match(r'(Category|UnknownReason|Severity)\.\w+ => "',t) or re.match(r'_ => "(OK)"',t):
                if 'Tag(' in t: continue
                out+=[x for x in re.findall(LIT,t)[:1] if not x.strip().isupper()]
    out=[x for x in out if not re.fullmatch(r'[A-Z0-9-]+',x)]
    seen=[];[seen.append(x) for x in out if x not in seen]
    return [x.replace('\\"','"') for x in seen]


def placeholders(text):
    return sorted(re.findall(r"\{[^}]*\}", text))


def main(root="."):
    wanted = templates(root)
    problems = 0
    for path in sorted(glob.glob(root + "/src/ShivaAssist.Core/Localization/*.json")):
        with open(path, encoding="utf-8") as handle:
            texts = json.load(handle)["texts"]
        name = path.split("/")[-1]
        for template in wanted:
            if template not in texts:
                print(f"{name}: missing translation: {template}")
                problems += 1
            elif placeholders(template) != placeholders(texts[template]):
                print(f"{name}: placeholders differ: {template}")
                problems += 1
        for key in texts:
            if key not in wanted:
                print(f"{name}: unused (warning): {key}")
    print(f"{len(wanted)} templates checked, {problems} problem(s).")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "."))
