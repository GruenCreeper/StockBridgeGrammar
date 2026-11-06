"""
Create a single Section Check -> Apply Grammar Rule example.

Run inside a GhPython or Rhino 8 Python 3 component with a Boolean input
named Run. Start with a blank Grasshopper canvas. Turn Run on once to
place the components, then turn it off.

Connections:
    Curves -> Recognize -> Section Check -> Apply Grammar Rule
    Example Stock -> Section Check.S and Apply.S
    Member IDs panel -> Section Check.Id
    Forces panel -> Section Check.F
    Section Check.G2/Nr/Nt -> Apply.G/R/T

The manual ID and force panels illustrate external force input. Replace
their values with forces calculated for the input graph.

The VGS Force Adapter is also placed with its G input connected. VGS is a
separate plugin. To use it, connect EdgeLn/EdgeMag to the adapter's Ln/Mag
inputs, then its Id/F outputs to Section Check.Id/F in place of the
manual panels.

Supply any anchors or target point required by the suggested rule.
Recompute forces after a geometry change before checking again. This
canvas does not implement a feedback loop or a force solver.
"""
import System
import clr
import Grasshopper
import Grasshopper.Kernel as ghk
from System.Drawing import PointF, RectangleF

SB = {
    "recognize":   System.Guid("12345678-1111-1111-1111-100000000001"),
    "apply":       System.Guid("12345678-3333-3333-3333-300000000003"),
    "stock":       System.Guid("12345678-6666-6666-6666-600000000006"),
    "check":       System.Guid("12345678-dddd-dddd-dddd-d00000000013"),
    "vgsf":        System.Guid("12345678-eeee-eeee-eeee-e00000000014"),
}
CURVE_GUID = System.Guid("d5967b9f-e8ee-436b-a8ad-29fdcecf32d5")
PANEL_GUID = System.Guid("59e0b89a-e487-49f8-bab8-b5bab16be14c")

POS = {
    "curves":      (120, 120),
    "recognize":   (340, 120),
    "stock":       (120, 360),
    "preset":      (-90, 360),
    "vgsf":        (560, 60),
    "check":       (780, 200),
    "apply":       (1040, 200),
    "ids_panel":   (560, 300),
    "forces_panel":(560, 410),
}


def emit(buf, msg):
    print(msg); buf.append(msg)


def get_doc():
    try:
        d = ghenv.Component.OnPingDocument()
        if d is not None: return d
    except Exception: pass
    try:
        c = Grasshopper.Instances.ActiveCanvas
        if c is not None and c.Document is not None: return c.Document
    except Exception: pass
    return None


def get_server():
    try: return Grasshopper.Instances.ComponentServer
    except Exception: return None


_proxy_cache = None
def find_proxy(server, guid):
    global _proxy_cache
    try:
        p = server.FindObjectByGuid(guid)
        if p is not None: return p
    except Exception: pass
    if _proxy_cache is None:
        try: _proxy_cache = list(server.ObjectProxies)
        except Exception: _proxy_cache = []
    for p in _proxy_cache:
        try:
            if p.Guid == guid: return p
        except Exception: continue
    return None


def instantiate(server, guid, pivot, log, label):
    obj = None
    proxy = find_proxy(server, guid)
    if proxy is not None:
        try: obj = proxy.CreateInstance()
        except Exception: obj = None
    if obj is None:
        try: obj = server.EmitObject(guid)
        except Exception as ex: emit(log, "  ! EmitObject failed {0}: {1}".format(label, ex)); return None
    if obj is None:
        emit(log, "  ! could not instantiate {0} (is the .gha loaded?)".format(label)); return None
    try:
        obj.CreateAttributes()
        obj.Attributes.Pivot = PointF(float(pivot[0]), float(pivot[1]))
    except Exception: pass
    return obj


try:
    from Grasshopper.Kernel import GH_Component as _GH_Component
except Exception:
    _GH_Component = None


def _is_param_only(comp):
    if _GH_Component is not None:
        try:
            if isinstance(comp, _GH_Component): return False
        except Exception: pass
    try:
        if "GH_Component" in (comp.GetType().FullName or ""): return False
    except Exception: pass
    return True


def _get_params(comp):
    try:
        v = comp.Params
        if v is not None: return v
    except Exception: pass
    try:
        prop = comp.GetType().GetProperty("Params")
        if prop is not None: return prop.GetValue(comp, None)
    except Exception: pass
    return None


def _iter(plist):
    try:
        for p in plist: yield p
        return
    except Exception: pass
    try:
        t = plist.GetType(); cnt = int(t.GetProperty("Count").GetValue(plist, None))
        item = t.GetProperty("Item")
        for i in range(cnt):
            yield item.GetValue(plist, System.Array[System.Object]([i]))
    except Exception:
        return


def _side(comp, which):
    params = _get_params(comp)
    if params is None: return None
    try:
        return getattr(params, which)
    except Exception:
        try:
            return params.GetType().GetProperty(which).GetValue(params, None)
        except Exception:
            return None


def find_output(comp, nick):
    if _is_param_only(comp): return comp
    lst = _side(comp, "Output")
    if lst is None: return None
    for p in _iter(lst):
        try:
            if p.NickName == nick: return p
        except Exception: continue
    return None


def find_input(comp, nick):
    if _is_param_only(comp): return comp
    lst = _side(comp, "Input")
    if lst is None: return None
    for p in _iter(lst):
        try:
            if p.NickName == nick: return p
        except Exception: continue
    return None


_addsource_mi = None
def _addsource_method():
    global _addsource_mi
    if _addsource_mi is None:
        try:
            from Grasshopper.Kernel import IGH_Param
            from System import Type, Array
            iface = clr.GetClrType(IGH_Param)
            _addsource_mi = iface.GetMethod("AddSource", Array[Type]([iface])) or False
        except Exception:
            _addsource_mi = False
    return _addsource_mi


def _add_source(dst, src):
    try:
        dst.AddSource(src); return True, None
    except Exception as ex:
        msg = str(ex)
        if "already has context" in msg or "Run Script" in msg: return True, None
        last = msg
    mi = _addsource_method()
    if mi:
        try:
            mi.Invoke(dst, System.Array[System.Object]([src])); return True, "(iface)"
        except Exception as ex2:
            last = str(getattr(ex2, "InnerException", None) or ex2)
    return False, last


def wire(src_comp, src_nick, dst_comp, dst_nick, log):
    src = find_output(src_comp, src_nick); dst = find_input(dst_comp, dst_nick)
    if src is None: emit(log, "  ! out not found {0}.{1}".format(getattr(src_comp,'NickName','?'), src_nick)); return
    if dst is None: emit(log, "  ! in not found {0}.{1}".format(getattr(dst_comp,'NickName','?'), dst_nick)); return
    ok, note = _add_source(dst, src)
    emit(log, "  {0} {1}.{2} -> {3}.{4}{5}".format("+" if ok else "!",
        getattr(src_comp,'NickName','?'), src_nick, getattr(dst_comp,'NickName','?'), dst_nick,
        "" if ok else " : "+str(note)))


def make_panel(server, text, pivot, log, w=150, h=60):
    proxy = find_proxy(server, PANEL_GUID)
    try: panel = proxy.CreateInstance()
    except Exception:
        try: panel = server.EmitObject(PANEL_GUID)
        except Exception as ex: emit(log, "  ! panel: {0}".format(ex)); return None
    panel.CreateAttributes()
    panel.Attributes.Pivot = PointF(float(pivot[0]), float(pivot[1]))
    try: panel.Attributes.Bounds = RectangleF(float(pivot[0]-w/2.0), float(pivot[1]-h/2.0), float(w), float(h))
    except Exception: pass
    try: panel.UserText = text
    except Exception:
        try: panel.SetUserText(text)
        except Exception: pass
    return panel


def _clear_previous(doc, log):
    guids = set(str(g) for g in SB.values())
    victims = []
    for o in doc.Objects:
        try:
            if str(o.ComponentGuid) in guids: victims.append(o)
        except Exception: continue
    n = 0
    for o in victims:
        try: doc.RemoveObject(o.Attributes, False); n += 1
        except Exception:
            try: doc.RemoveObject(o, False); n += 1
            except Exception: pass
    if n: emit(log, "Cleared {0} StockBridge component(s) from a previous run.".format(n))


def _populate(doc, log):
    _clear_previous(doc, log)
    server = get_server()
    if server is None: emit(log, "No ComponentServer."); return
    made = {}
    def add(obj, key):
        if obj is None: return None
        made[key] = obj
        try: doc.AddObject(obj, False)
        except Exception as ex:
            if not ("already has context" in str(ex) or "Run Script" in str(ex)):
                emit(log, "  ! AddObject {0}: {1} (kept)".format(key, ex))
        return obj

    emit(log, "Placing components...")
    cur = instantiate(server, CURVE_GUID, POS["curves"], log, "Curves")
    if cur is not None:
        try: cur.NickName = "Curves"
        except Exception: pass
        add(cur, "curves"); emit(log, "  placed Curves")
    for key in ("recognize", "stock", "vgsf", "check", "apply"):
        c = instantiate(server, SB[key], POS[key], log, key)
        if c is not None: add(c, key); emit(log, "  placed " + key)

    preset = make_panel(server, "long", POS["preset"], log, 120, 28)
    if preset is not None: add(preset, "preset")
    ids_p = make_panel(server, "M0", POS["ids_panel"], log, 170, 60)
    if ids_p is not None: add(ids_p, "ids_panel")
    frc_p = make_panel(server, "250", POS["forces_panel"], log, 170, 60)
    if frc_p is not None: add(frc_p, "forces_panel")

    emit(log, "Wiring...")
    g = made.get
    if g("preset") and g("stock"):
        di = find_input(g("stock"), "P")
        if di is not None: _add_source(di, g("preset"))
    if g("curves") and g("recognize"): wire(g("curves"), "C", g("recognize"), "C", log)
    if g("recognize") and g("check"):  wire(g("recognize"), "J", g("check"), "G", log)
    if g("recognize") and g("vgsf"):   wire(g("recognize"), "J", g("vgsf"), "G", log)
    if g("stock") and g("check"):      wire(g("stock"), "J", g("check"), "S", log)
    if g("stock") and g("apply"):      wire(g("stock"), "J", g("apply"), "S", log)
    if g("ids_panel") and g("check"):
        di = find_input(g("check"), "Id")
        if di is not None: _add_source(di, g("ids_panel"))
        emit(log, "  + Panel(ids) -> check.Id")
    if g("forces_panel") and g("check"):
        di = find_input(g("check"), "F")
        if di is not None: _add_source(di, g("forces_panel"))
        emit(log, "  + Panel(forces) -> check.F")
    if g("check") and g("apply"):
        wire(g("check"), "G2", g("apply"), "G", log)
        wire(g("check"), "Nr", g("apply"), "R", log)
        wire(g("check"), "Nt", g("apply"), "T", log)

    emit(log, "Done. Set Run=False, delete this script. Add VGS by hand and wire")
    emit(log, "  VGS EdgeLn->SBVgsF.Ln, EdgeMag->SBVgsF.Mag, then SBVgsF.Id/F->SBCheck.Id/F.")
    emit(log, "Edit the M0 / 250 panels to matching member IDs and forces (kN, negative=compression).")
    emit(log, "Provide the suggested rule's anchors/target point; recalculate forces after each geometry change.")


def main():
    log = []
    try: run = bool(Run)
    except NameError:
        emit(log, "Add a 'Run' (bool) input, wire a True toggle."); return "\n".join(log)
    if not run:
        emit(log, "Run = False. Toggle True to build."); return "\n".join(log)
    doc = get_doc()
    if doc is None: emit(log, "No active GH document."); return "\n".join(log)
    try: _populate(doc, log)
    except Exception as ex:
        if "already has context" in str(ex) or "Run Script" in str(ex):
            emit(log, "(re-entry warning suppressed — placement OK)")
        else: emit(log, "placement raised: {0}".format(ex))
    return "\n".join(log)


a = main()
