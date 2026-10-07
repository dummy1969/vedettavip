#!/usr/bin/env python3
"""
Genera src/VedettaVip.Api/Resources/oui.tsv.gz (prefisso esadecimale <TAB> produttore) dai registri pubblici IEEE:
MA-L (24 bit), MA-M (28 bit), MA-S (36 bit). Usato dalla Discovery per il produttore dal MAC.

Aggiornare (una o due volte l'anno):  python3 third-party/ieee-oui/generate.py   (dalla radice del repository)
Scarica i CSV in una cartella temporanea; con --from <cartella> usa CSV già scaricati.
"""
import csv, gzip, io, pathlib, sys, tempfile, urllib.request, datetime

SOURCES = {
    "oui.csv": "https://standards-oui.ieee.org/oui/oui.csv",
    "mam.csv": "https://standards-oui.ieee.org/oui28/mam.csv",
    "oui36.csv": "https://standards-oui.ieee.org/oui36/oui36.csv",
}
root = pathlib.Path(__file__).resolve().parent
out = root.parent.parent / "src/VedettaVip.Api/Resources/oui.tsv.gz"

folder = pathlib.Path(sys.argv[sys.argv.index("--from") + 1]) if "--from" in sys.argv else pathlib.Path(tempfile.mkdtemp())
for name, url in SOURCES.items():
    if not (folder / name).exists():
        req = urllib.request.Request(url, headers={"User-Agent": "VedettaVip OUI generator"})
        (folder / name).write_bytes(urllib.request.urlopen(req, timeout=120).read())

entries = {}
for name in SOURCES:
    with open(folder / name, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            prefix = row["Assignment"].strip().upper()
            org = " ".join(row["Organization Name"].split())
            if prefix and org and org.upper() != "PRIVATE":
                entries[prefix] = org

lines = "".join(f"{p}\t{entries[p]}\n" for p in sorted(entries))
with gzip.GzipFile(out, "wb", mtime=0) as gz:  # mtime fisso: stesso file a parità di dati
    gz.write(lines.encode("utf-8"))
(root / "VERSION").write_text(datetime.date.today().isoformat() + f" ({len(entries)} prefissi)\n")
print(f"{len(entries)} prefissi → {out} ({out.stat().st_size // 1024} KB)")
