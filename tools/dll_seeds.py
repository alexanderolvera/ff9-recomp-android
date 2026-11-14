import json, sys, pefile
p = pefile.PE(sys.argv[1])
base = p.OPTIONAL_HEADER.ImageBase
seeds = [{"address": hex(base + e.address), "name": e.name.decode()} for e in p.DIRECTORY_ENTRY_EXPORT.symbols]
json.dump(seeds, open(sys.argv[2], "w"), indent=1)
print(hex(base), p.FILE_HEADER.Machine, len(seeds), "exports;", [ (s.Name.rstrip(b'\0').decode(), hex(s.VirtualAddress), s.Misc_VirtualSize) for s in p.sections])
print(sorted({i.dll.decode() for i in p.DIRECTORY_ENTRY_IMPORT}), sum(len(i.imports) for i in p.DIRECTORY_ENTRY_IMPORT), "imported functions")
