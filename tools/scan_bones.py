import sys, struct, json, UnityPy
data = sys.argv[1]
out = sys.argv[2]
env = UnityPy.load(data + "/resources.assets")

class R:
    def __init__(s, b): s.b, s.p = b, 0
    def i32(s):
        v = struct.unpack_from("<i", s.b, s.p)[0]; s.p += 4; return v
    def skip(s, n): s.p += n
    def align(s): s.p = (s.p + 3) & ~3
    def str(s):
        n = s.i32(); v = s.b[s.p:s.p + n].decode("utf-8"); s.p += n; s.align(); return v

atlases = {}
for obj in env.objects:
    if obj.type.name != "MonoBehaviour":
        continue
    n = obj.peek_name()
    if not (n and n.startswith("D_Atlas_")):
        continue
    r = R(obj.get_raw_data())
    r.skip(12); r.skip(4); r.skip(12)
    name = r.str(); species = r.str(); r.str(); r.str(); r.str()
    r.skip(12 + 16)
    support_parts = r.i32(); hammer = r.i32()
    count = r.i32()
    bones = []
    for _ in range(count):
        bones.append(r.str()); r.skip(40)
    atlases[species] = {"support_parts": support_parts, "hammer_points": hammer, "bones": bones}
    print(species, count, "supports", support_parts, "hammer", hammer, "end", r.p, len(r.b))
print("total bones", sum(len(a["bones"]) for a in atlases.values()))
json.dump(atlases, open(out, "w"), indent=1)
