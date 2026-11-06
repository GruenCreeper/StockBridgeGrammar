"""
Create a split-and-articulate Grasshopper example.

The workflow recognises curves, assigns stock, selects a mismatched
member, and applies splitMemberByStockLength followed by
articulateChainToStock. Decode Graph previews the result.

The split uses maxBridgesPerNode=0. The articulation uses the available
stock lengths to fold the split chain while retaining its endpoints.

Install StockBridgeGrammar and start with a blank Grasshopper canvas.
In a GhPython or Rhino 8 Python 3 component, add one Boolean input named
Run, paste this script, and connect a Boolean Toggle. Turn Run on once,
then turn it off. Set the Curve parameter to the desired Rhino curves.

Read output a for placement and wiring diagnostics. Inspect the Apply
history and warnings before using or baking Decode's Member Lines.
"""

import System
import clr
import Grasshopper
import Grasshopper.Kernel as ghk
from System.Drawing import PointF, RectangleF

try:
    from Grasshopper.Kernel import GH_Component as _GH_Component
except Exception:
    _GH_Component = None

# ------------------------------------------------------------------
# Component GUIDs — must match StockBridgeGrammar's ComponentGuid.
# ------------------------------------------------------------------
SB = {
    "recognize":     System.Guid("12345678-1111-1111-1111-100000000001"),
    "assign":        System.Guid("12345678-2222-2222-2222-200000000002"),
    "apply":         System.Guid("12345678-3333-3333-3333-300000000003"),
    "decode":        System.Guid("12345678-5555-5555-5555-500000000005"),
    "example_stock": System.Guid("12345678-6666-6666-6666-600000000006"),
    "target_picker": System.Guid("12345678-bbbb-bbbb-bbbb-b00000000011"),
}

CURVE_PARAM_GUID = System.Guid("d5967b9f-e8ee-436b-a8ad-29fdcecf32d5")
PANEL_GUID       = System.Guid("59e0b89a-e487-49f8-bab8-b5bab16be14c")

# Pivot positions (x, y) on the canvas.
POS = {
    "curves":          (160, 280),
    "example_stock":   (160, 480),
    "recognize":       (380, 280),
    "assign":          (620, 280),
    "target_picker":   (620, 480),
    "panel_split_id":  (620, 120),
    "apply1":          (880, 280),
    "panel_split_p":   (880, 120),
    "panel_artic_id":  (1140, 120),
    "apply2":          (1140, 280),
    "decode":          (1400, 280),
}

SPLIT_ID  = "splitMemberByStockLength"
ARTIC_ID  = "articulateChainToStock"
SPLIT_P   = '{"maxBridgesPerNode":0}'   # clean chain, no braces fighting the fold


def get_doc():
    try:
        doc = ghenv.Component.OnPingDocument()
        if doc is not None:
            return doc
    except Exception:
        pass
    try:
        canvas = Grasshopper.Instances.ActiveCanvas
        if canvas is not None and canvas.Document is not None:
            return canvas.Document
    except Exception:
        pass
    return None


def get_server():
    try:
        return Grasshopper.Instances.ComponentServer
    except Exception:
        pass
    try:
        return ghk.GH_ComponentServer
    except Exception:
        pass
    return None


def emit(buf, msg):
    print(msg)
    buf.append(msg)


_proxy_cache = None

def _all_proxies(server):
    global _proxy_cache
    if _proxy_cache is None:
        try:
            _proxy_cache = list(server.ObjectProxies)
        except Exception:
            _proxy_cache = []
    return _proxy_cache


def find_proxy(server, guid):
    try:
        proxy = server.FindObjectByGuid(guid)
        if proxy is not None:
            return proxy
    except Exception:
        pass
    for p in _all_proxies(server):
        try:
            if p.Guid == guid:
                return p
        except Exception:
            continue
    return None


def instantiate(server, guid, pivot, log, label):
    proxy = find_proxy(server, guid)
    obj = None
    if proxy is not None:
        try:
            obj = proxy.CreateInstance()
        except Exception as ex:
            emit(log, "  ! CreateInstance failed for {0}: {1}".format(label, ex))
            obj = None
    if obj is None:
        try:
            obj = server.EmitObject(guid)
        except Exception as ex:
            emit(log, "  ! EmitObject failed for {0}: {1}".format(label, ex))
            obj = None
    if obj is None:
        emit(log, "  ! could not instantiate {0} (guid {1}) — is "
                  "StockBridgeGrammar.gha loaded?".format(label, guid))
        return None
    try:
        obj.CreateAttributes()
        obj.Attributes.Pivot = PointF(float(pivot[0]), float(pivot[1]))
    except Exception as ex:
        emit(log, "  ! Attributes failed for {0}: {1}".format(label, ex))
    return obj


def _get_params(comp):
    try:
        v = comp.Params
        if v is not None:
            return v
    except Exception:
        pass
    try:
        t = comp.GetType()
        prop = t.GetProperty("Params")
        if prop is not None:
            v = prop.GetValue(comp, None)
            if v is not None:
                return v
    except Exception:
        pass
    if _GH_Component is not None:
        try:
            if isinstance(comp, _GH_Component):
                return comp.Params
        except Exception:
            pass
    return None


def _is_igh_param_only(comp):
    if _GH_Component is not None:
        try:
            if isinstance(comp, _GH_Component):
                return False
        except Exception:
            pass
    try:
        tname = comp.GetType().FullName or ""
        if "GH_Component" in tname:
            return False
    except Exception:
        pass
    return True


def _iter_params_list(plist):
    try:
        for p in plist:
            yield p
        return
    except Exception:
        pass
    try:
        t = plist.GetType()
        count = int(t.GetProperty("Count").GetValue(plist, None))
        item_prop = t.GetProperty("Item")
        for i in range(count):
            try:
                yield item_prop.GetValue(plist, System.Array[System.Object]([i]))
            except Exception:
                continue
    except Exception:
        return


def find_input(comp, nick):
    if _is_igh_param_only(comp):
        return comp
    params = _get_params(comp)
    if params is None:
        return None
    try:
        in_list = params.Input
    except Exception:
        try:
            in_list = params.GetType().GetProperty("Input").GetValue(params, None)
        except Exception:
            return None
    for p in _iter_params_list(in_list):
        try:
            if p.NickName == nick:
                return p
        except Exception:
            continue
    return None


def find_output(comp, nick):
    if _is_igh_param_only(comp):
        return comp
    params = _get_params(comp)
    if params is None:
        return None
    try:
        out_list = params.Output
    except Exception:
        try:
            out_list = params.GetType().GetProperty("Output").GetValue(params, None)
        except Exception:
            return None
    for p in _iter_params_list(out_list):
        try:
            if p.NickName == nick:
                return p
        except Exception:
            continue
    return None


def _available(comp, which):
    if _is_igh_param_only(comp):
        return "(IGH_Param — comp itself)"
    params = _get_params(comp)
    if params is None:
        return "(Params unreachable)"
    try:
        lst = getattr(params, which)
    except Exception:
        try:
            lst = params.GetType().GetProperty(which).GetValue(params, None)
        except Exception:
            return "(list unreachable)"
    names = []
    for p in _iter_params_list(lst):
        try: names.append(p.NickName)
        except Exception: names.append("?")
    return ", ".join(names) if names else "(empty)"


_addsource_mi = None

def _addsource_method():
    """MethodInfo for IGH_Param.AddSource(IGH_Param), taken from the
    INTERFACE type. Under Rhino 8 Python 3 (pythonnet) the param objects are
    surfaced as the IGH_DocumentObject interface, which hides AddSource and
    makes instance.GetType() report the interface (so concrete-type reflection
    finds nothing). Resolving the method off IGH_Param itself and Invoking on
    the instance dispatches correctly because the object DOES implement it."""
    global _addsource_mi
    if _addsource_mi is None:
        try:
            from Grasshopper.Kernel import IGH_Param
            from System import Type, Array
            iface = clr.GetClrType(IGH_Param)
            sig = Array[Type]([iface])
            _addsource_mi = iface.GetMethod("AddSource", sig) or False
        except Exception:
            _addsource_mi = False
    return _addsource_mi


def _call_add_source(dst_param, src_param):
    # 1) direct (works under IronPython GhPython)
    try:
        dst_param.AddSource(src_param)
        return True, None
    except Exception as ex:
        msg = str(ex)
        if ("already has context" in msg) or ("Run Script" in msg):
            return True, "(re-entry warning suppressed)"
        last_err = msg
    # 2) interface MethodInfo invoke (works under Rhino 8 Python 3 / pythonnet)
    mi = _addsource_method()
    if mi:
        try:
            mi.Invoke(dst_param, System.Array[System.Object]([src_param]))
            return True, "(via IGH_Param interface)"
        except Exception as ex2:
            last_err = str(getattr(ex2, "InnerException", None) or ex2)
    # 3) concrete-type reflection (last resort)
    try:
        from System.Reflection import BindingFlags
        t = dst_param.GetType()
        flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        for mth in t.GetMethods(flags):
            if mth.Name == "AddSource" and len(mth.GetParameters()) == 1:
                try:
                    mth.Invoke(dst_param, System.Array[System.Object]([src_param]))
                    return True, "(via reflection)"
                except Exception as ex3:
                    last_err = str(ex3)
                    continue
    except Exception as ex:
        last_err = str(ex)
    return False, last_err


def wire(src_comp, src_nick, dst_comp, dst_nick, log):
    src = find_output(src_comp, src_nick)
    dst = find_input(dst_comp, dst_nick)
    if src is None:
        emit(log, "  ! source not found: {0}.{1}  (outs: {2})".format(
            getattr(src_comp, "NickName", "?"), src_nick, _available(src_comp, "Output")))
        return
    if dst is None:
        emit(log, "  ! target not found: {0}.{1}  (ins: {2})".format(
            getattr(dst_comp, "NickName", "?"), dst_nick, _available(dst_comp, "Input")))
        return
    ok, note = _call_add_source(dst, src)
    s_nn = getattr(src_comp, "NickName", "?")
    d_nn = getattr(dst_comp, "NickName", "?")
    if ok:
        emit(log, "  + {0}.{1} -> {2}.{3}{4}".format(
            s_nn, src_nick, d_nn, dst_nick, " " + note if note else ""))
    else:
        emit(log, "  ! wire failed {0}.{1}->{2}.{3}: {4}".format(
            s_nn, src_nick, d_nn, dst_nick, note))


def make_panel(server, text, pivot, log, w=210, h=28):
    proxy = find_proxy(server, PANEL_GUID)
    if proxy is None:
        emit(log, "  ! Panel GUID not found")
        return None
    try:
        panel = proxy.CreateInstance()
    except Exception:
        try:
            panel = server.EmitObject(PANEL_GUID)
        except Exception as ex:
            emit(log, "  ! Panel instantiate failed: {0}".format(ex))
            return None
    panel.CreateAttributes()
    panel.Attributes.Pivot = PointF(float(pivot[0]), float(pivot[1]))
    try:
        panel.Attributes.Bounds = RectangleF(
            float(pivot[0] - w / 2.0), float(pivot[1] - h / 2.0), float(w), float(h))
    except Exception:
        pass
    ok = False
    try:
        panel.UserText = text
        ok = True
    except Exception:
        pass
    if not ok:
        try:
            panel.SetUserText(text)
            ok = True
        except Exception:
            pass
    if not ok:
        try:
            from Grasshopper.Kernel.Types import GH_String
            panel.UserText = ""
            panel.AddVolatileData(Grasshopper.Kernel.Data.GH_Path(0), 0, GH_String(text))
            ok = True
        except Exception as ex:
            emit(log, "  ! could not set Panel text: {0}".format(ex))
    return panel


def _clear_previous(doc, log):
    """Remove any StockBridge components left from a previous run so a
    re-run rebuilds cleanly instead of bailing or duplicating."""
    sb_guids = set(str(g) for g in SB.values())
    victims = []
    for obj in doc.Objects:
        try:
            if str(obj.ComponentGuid) in sb_guids:
                victims.append(obj)
        except Exception:
            continue
    n = 0
    for obj in victims:
        try:
            doc.RemoveObject(obj.Attributes, False)
            n += 1
        except Exception:
            try:
                doc.RemoveObject(obj, False)
                n += 1
            except Exception:
                pass
    if n:
        emit(log, "Cleared {0} StockBridge component(s) from a previous run.".format(n))


def _populate(doc, log):
    _clear_previous(doc, log)

    server = get_server()
    if server is None:
        emit(log, "Could not find Grasshopper.Instances.ComponentServer.")
        return

    emit(log, "Building split -> articulate battery chain...")
    created = {}

    def safe_add(obj, key):
        # Keep the reference FIRST: GH often still adds the object even when
        # AddObject throws a (cosmetic) re-entry error, and we must be able to
        # wire it afterwards. Without this, a throw would drop the component
        # from `created` and every wire touching it gets silently skipped —
        # i.e. components appear on the canvas but with no wires.
        created[key] = obj
        try:
            doc.AddObject(obj, False)
            return True
        except Exception as ex:
            msg = str(ex)
            if ("already has context" in msg) or ("Run Script" in msg):
                return True
            emit(log, "  ! AddObject reported: {0}: {1} (kept for wiring)".format(key, msg))
            return False

    # Curve parameter for Rhino selection.
    cv = instantiate(server, CURVE_PARAM_GUID, POS["curves"], log, "Curve param")
    if cv is not None:
        try: cv.NickName = "Curves"
        except Exception: pass
        if safe_add(cv, "curves"):
            emit(log, "  placed Curve param")

    # StockBridge components.
    for key in ["recognize", "example_stock", "assign", "target_picker"]:
        comp = instantiate(server, SB[key], POS[key], log, key)
        if comp is not None and safe_add(comp, key):
            emit(log, "  placed {0}".format(key))

    # Two Apply components (same GUID, two instances) + one Decode.
    a1 = instantiate(server, SB["apply"], POS["apply1"], log, "apply1")
    if a1 is not None and safe_add(a1, "apply1"):
        emit(log, "  placed Apply #1 (split)")
    a2 = instantiate(server, SB["apply"], POS["apply2"], log, "apply2")
    if a2 is not None and safe_add(a2, "apply2"):
        emit(log, "  placed Apply #2 (articulate)")
    dc = instantiate(server, SB["decode"], POS["decode"], log, "decode")
    if dc is not None and safe_add(dc, "decode"):
        emit(log, "  placed Decode")

    # Panels: two rule ids + split params.
    p_sid = make_panel(server, SPLIT_ID, POS["panel_split_id"], log)
    if p_sid is not None and safe_add(p_sid, "panel_split_id"):
        emit(log, "  placed Panel (split rule id)")
    p_sp = make_panel(server, SPLIT_P, POS["panel_split_p"], log)
    if p_sp is not None and safe_add(p_sp, "panel_split_p"):
        emit(log, "  placed Panel (split params)")
    p_aid = make_panel(server, ARTIC_ID, POS["panel_artic_id"], log)
    if p_aid is not None and safe_add(p_aid, "panel_artic_id"):
        emit(log, "  placed Panel (articulate rule id)")

    # ----- Wires -----
    emit(log, "Wiring...")
    g = created.get

    if g("curves") and g("recognize"):
        wire(g("curves"), "C", g("recognize"), "C", log)
    if g("recognize") and g("assign"):
        wire(g("recognize"), "J", g("assign"), "G", log)
    if g("example_stock") and g("assign"):
        wire(g("example_stock"), "J", g("assign"), "S", log)
    if g("assign") and g("target_picker"):
        wire(g("assign"), "G", g("target_picker"), "G", log)
        wire(g("assign"), "M", g("target_picker"), "M", log)

    # Apply #1 — split
    if g("assign") and g("apply1"):
        wire(g("assign"), "G", g("apply1"), "G", log)
    if g("example_stock") and g("apply1"):
        wire(g("example_stock"), "J", g("apply1"), "S", log)
    if g("target_picker") and g("apply1"):
        wire(g("target_picker"), "T", g("apply1"), "T", log)
    if g("panel_split_id") and g("apply1"):
        wire(g("panel_split_id"), "", g("apply1"), "R", log)
    if g("panel_split_p") and g("apply1"):
        wire(g("panel_split_p"), "", g("apply1"), "P", log)

    # Apply #2 — articulate (chained off Apply #1's Updated Graph)
    if g("apply1") and g("apply2"):
        wire(g("apply1"), "G", g("apply2"), "G", log)
    if g("example_stock") and g("apply2"):
        wire(g("example_stock"), "J", g("apply2"), "S", log)
    if g("target_picker") and g("apply2"):
        wire(g("target_picker"), "T", g("apply2"), "T", log)
    if g("panel_artic_id") and g("apply2"):
        wire(g("panel_artic_id"), "", g("apply2"), "R", log)

    # Decode the final graph
    if g("apply2") and g("decode"):
        wire(g("apply2"), "G", g("decode"), "G", log)

    emit(log, "Done. Set Run = False, then delete this script.")
    emit(log, "Next: set your bridge Curves, then bake Decode's L output.")


def main():
    log = []
    try:
        run_flag = bool(Run)
    except NameError:
        emit(log, "Add a 'Run' (bool) input, then wire a Boolean Toggle = True.")
        return "\n".join(log)
    if not run_flag:
        emit(log, "Run = False. Toggle to True to build the canvas.")
        return "\n".join(log)
    doc = get_doc()
    if doc is None:
        emit(log, "Could not find the active Grasshopper document.")
        return "\n".join(log)
    try:
        _populate(doc, log)
    except Exception as ex:
        msg = str(ex)
        if "already has context" in msg or "Run Script" in msg:
            emit(log, "(re-entry warning suppressed — placement OK)")
        else:
            emit(log, "Inline placement raised: {0}".format(msg))
    emit(log, "")
    emit(log, "Toggle Run back to False so this script doesn't re-run.")
    return "\n".join(log)


a = main()
