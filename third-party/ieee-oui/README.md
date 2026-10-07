# Registri IEEE dei produttori (OUI)

Prefissi MAC assegnati dall'IEEE Registration Authority (MA-L, MA-M, MA-S), dati pubblici scaricati da
standards-oui.ieee.org. `generate.py` li riduce a "prefisso → nome dell'organizzazione" in
`src/VedettaVip.Api/Resources/oui.tsv.gz` (risorsa incorporata nell'API): la Discovery mostra il produttore degli host
trovati via ARP o vicini e lo usa per dedurre il tipo. Data dell'ultimo aggiornamento in `VERSION`.
