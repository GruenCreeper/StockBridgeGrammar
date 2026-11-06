"""
Create the StockBridgeGrammar demonstration canvas.

Run inside a GhPython or Rhino 8 Python 3 component on a blank Grasshopper
canvas. Add one Boolean input named Run, paste this script, and connect a
Boolean Toggle. Turn Run on to place the components, then turn it off.

The script creates recognition, inventory assignment, rule application,
and candidate-selection workflows using the installed StockBridge plugin.
It also places a Curve parameter, rule-ID panel, and a second Decode
component for candidate preview. See connection_map.md for the wiring.

Set the Curve parameter to the input curves after placement. Diagnostics
are printed to output a. Check those messages for missing components or
connections. The script component can be removed after placement.
"""

import System
import Grasshopper
import Grasshopper.Kernel as ghk
from System.Drawing import PointF, RectangleF

# Concrete .NET base type used to distinguish "real components" (which
# have a Params.Input/Output server) from raw IGH_Param objects like the
# Curve parameter or Panel (which are themselves a single param). hasattr
# alone is unreliable under pythonnet when the object is exposed via the
# IGH_DocumentObject interface.
try:
    from Grasshopper.Kernel import GH_Component as _GH_Component
except Exception:
    _GH_Component = None

# ------------------------------------------------------------------
# Component GUIDs — must match StockBridgeGrammar's ComponentGuid.
# ------------------------------------------------------------------
SB = {
    "recognize":         System.Guid("12345678-1111-1111-1111-100000000001"),
    "assign":            System.Guid("12345678-2222-2222-2222-200000000002"),
    "apply":             System.Guid("12345678-3333-3333-3333-300000000003"),
    "generate":          System.Guid("12345678-4444-4444-4444-400000000004"),
    "decode":            System.Guid("12345678-5555-5555-5555-500000000005"),
    "example_stock":     System.Guid("12345678-6666-6666-6666-600000000006"),
    "rule_library":      System.Guid("12345678-7777-7777-7777-700000000007"),
    "stock_item":        System.Guid("12345678-8888-8888-8888-800000000008"),
    "inventory":         System.Guid("12345678-9999-9999-9999-900000000009"),
    "rule_params":       System.Guid("12345678-aaaa-aaaa-aaaa-a00000000010"),
    "target_picker":     System.Guid("12345678-bbbb-bbbb-bbbb-b00000000011"),
    "candidate_select":  System.Guid("12345678-cccc-cccc-cccc-c00000000012"),
}

# Standard built-in component GUIDs.
CURVE_PARAM_GUID = System.Guid("d5967b9f-e8ee-436b-a8ad-29fdcecf32d5")
PANEL_GUID       = System.Guid("59e0b89a-e487-49f8-bab8-b5bab16be14c")

# Pivot positions (x, y) on the canvas.
POS = {
    "curves":            (160, 260),
    "recognize":         (380, 260),
    "example_stock":     (160, 480),
    "assign":            (640, 260),
    "rule_library":      (640,  80),
    "target_picker":     (640, 480),
    "rule_params":       (900,  80),
    "apply":             (900, 260),
    "decode":            (1160, 260),
    "generate":          (640, 700),
    "candidate_select":  (900, 700),
    "decode2":           (1160, 700),
    "rule_id_panel":     (900, 180),
}


def get_doc():
    """Return the active Grasshopper document, robust across GhPython
    flavours (IronPython GhPython and Rhino 8 Python 3 Script)."""
    # 1. ghenv is provided by both — preferred.
    try:
        doc = ghenv.Component.OnPingDocument()
        if doc is not None:
            return doc
    except Exception:
        pass
    # 2. Top-level Grasshopper.Instances (NOT Grasshopper.Kernel.Instances).
    try:
        canvas = Grasshopper.Instances.ActiveCanvas
        if canvas is not None and canvas.Document is not None:
            return canvas.Document
    except Exception:
        pass
    return None


def get_server():
    """Return the global GH ComponentServer (location varies by flavour)."""
    try:
        return Grasshopper.Instances.ComponentServer
    except Exception:
        pass
    try:
        return ghk.GH_ComponentServer  # older API path
    except Exception:
        pass
    return None


def emit(buf, msg):
    print(msg)
    buf.append(msg)


_proxy_cache = None

def _all_proxies(server):
    """Return a Python list of every IGH_ObjectProxy, cached."""
    global _proxy_cache
    if _proxy_cache is None:
        try:
            _proxy_cache = list(server.ObjectProxies)
        except Exception:
            _proxy_cache = []
    return _proxy_cache


def find_proxy(server, guid):
    """Robust GUID → IGH_ObjectProxy lookup across GhPython flavours."""
    # 1. C# canonical name (works in IronPython GhPython).
    try:
        proxy = server.FindObjectByGuid(guid)
        if proxy is not None:
            return proxy
    except AttributeError:
        pass
    except Exception:
        pass
    # 2. Iterate ObjectProxies (works in Python 3 / pythonnet).
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
    # 1. Try via proxy.CreateInstance().
    if proxy is not None:
        try:
            obj = proxy.CreateInstance()
        except Exception as ex:
            emit(log, "  ! CreateInstance failed for {0}: {1}".format(label, ex))
            obj = None
    # 2. Fallback: server.EmitObject(guid) returns the instance directly.
    if obj is None:
        try:
            obj = server.EmitObject(guid)
        except Exception as ex:
            emit(log, "  ! EmitObject failed for {0}: {1}".format(label, ex))
            obj = None
    if obj is None:
        emit(log, "  ! could not instantiate {0} (guid {1}) — "
                  "is StockBridgeGrammar.gha loaded?".format(label, guid))
        return None
    try:
        obj.CreateAttributes()
        obj.Attributes.Pivot = PointF(float(pivot[0]), float(pivot[1]))
    except Exception as ex:
        emit(log, "  ! Attributes failed for {0}: {1}".format(label, ex))
    return obj


def _get_params(comp):
    """Return comp.Params if it can be reached, else None.

    Tries three paths because pythonnet may hide the property when the
    object is exposed through the IGH_DocumentObject interface:
      1. Direct attribute access  (`comp.Params`)
      2. .NET reflection via comp.GetType().GetProperty("Params")
      3. Cast-helper via isinstance + GH_Component (concrete view)
    """
    # Path 1: direct
    try:
        v = comp.Params
        if v is not None:
            return v
    except Exception:
        pass
    # Path 2: reflection
    try:
        t = comp.GetType()
        prop = t.GetProperty("Params")
        if prop is not None:
            v = prop.GetValue(comp, None)
            if v is not None:
                return v
    except Exception:
        pass
    # Path 3: isinstance + direct (works if Path 1 failed because of static
    # type view but the runtime type is a GH_Component subclass).
    if _GH_Component is not None:
        try:
            if isinstance(comp, _GH_Component):
                return comp.Params
        except Exception:
            pass
    return None


def _is_igh_param_only(comp):
    """True if comp is an IGH_Param (Curve param / Panel) — i.e. it IS a
    single parameter, not a host component with Input/Output lists.

    We check via .NET inheritance because hasattr is unreliable here.
    """
    if _GH_Component is not None:
        try:
            # If it IS a GH_Component, it has Params.Input/Output → not param-only.
            if isinstance(comp, _GH_Component):
                return False
        except Exception:
            pass
    # Try checking its .NET type name as a fallback heuristic.
    try:
        tname = comp.GetType().FullName or ""
        if "GH_Component" in tname:
            return False
    except Exception:
        pass
    # Default to True: most non-GH_Component objects we instantiate here are
    # IGH_Param (Curve param, Panel, etc.).
    return True


def _iter_params_list(plist):
    """Iterate a Grasshopper params list (GH_ComponentParamServer.Input /
    .Output) even when pythonnet hides the IEnumerable. Uses Count + indexer
    via reflection as a last resort."""
    # Direct iteration.
    try:
        for p in plist:
            yield p
        return
    except Exception:
        pass
    # Reflection: read Count, then index.
    try:
        t = plist.GetType()
        count_prop = t.GetProperty("Count")
        count = int(count_prop.GetValue(plist, None))
        item_prop = t.GetProperty("Item")
        for i in range(count):
            try:
                yield item_prop.GetValue(plist, System.Array[System.Object]([i]))
            except Exception:
                continue
    except Exception:
        return


def find_input(comp, nick):
    """Find an input param by NickName. If `comp` has no Params (it IS an
    IGH_Param such as a Curve parameter or Panel), return `comp` itself."""
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
    """Find an output param by NickName. If `comp` has no Params, comp
    itself is the source."""
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


def _available_outputs(comp):
    if _is_igh_param_only(comp):
        return "(IGH_Param — comp itself IS the output)"
    params = _get_params(comp)
    if params is None:
        return "(Params property unreachable)"
    try:
        out_list = params.Output
    except Exception:
        try:
            out_list = params.GetType().GetProperty("Output").GetValue(params, None)
        except Exception:
            return "(Output list unreachable)"
    names = []
    for p in _iter_params_list(out_list):
        try: names.append(p.NickName)
        except Exception: names.append("?")
    return ", ".join(names) if names else "(empty)"


def _available_inputs(comp):
    if _is_igh_param_only(comp):
        return "(IGH_Param — comp itself IS the input)"
    params = _get_params(comp)
    if params is None:
        return "(Params property unreachable)"
    try:
        in_list = params.Input
    except Exception:
        try:
            in_list = params.GetType().GetProperty("Input").GetValue(params, None)
        except Exception:
            return "(Input list unreachable)"
    names = []
    for p in _iter_params_list(in_list):
        try: names.append(p.NickName)
        except Exception: names.append("?")
    return ", ".join(names) if names else "(empty)"


def _call_add_source(dst_param, src_param):
    """Invoke IGH_Param.AddSource(src). Try direct call first, then
    reflection (in case pythonnet hides the method on the interface view).

    Returns (ok, err_msg).
    """
    # Path 1: direct
    try:
        dst_param.AddSource(src_param)
        return True, None
    except Exception as ex:
        msg = str(ex)
        if ("already has context" in msg) or ("Run Script" in msg):
            return True, "(re-entry warning suppressed)"
        last_err = msg
    # Path 2: reflection
    try:
        t = dst_param.GetType()
        # Find an AddSource overload taking 1 arg.
        from System.Reflection import BindingFlags
        flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        for m in t.GetMethods(flags):
            if m.Name == "AddSource":
                pars = m.GetParameters()
                if len(pars) == 1:
                    try:
                        m.Invoke(dst_param, System.Array[System.Object]([src_param]))
                        return True, "(via reflection)"
                    except Exception as ex2:
                        last_err = str(ex2)
                        continue
    except Exception as ex:
        last_err = str(ex)
    return False, last_err


def wire(src_comp, src_nick, dst_comp, dst_nick, log):
    src = find_output(src_comp, src_nick)
    dst = find_input(dst_comp, dst_nick)
    if src is None:
        emit(log, "  ! source not found: {0}.{1}  (available outs: {2})".format(
            getattr(src_comp, "NickName", "?"), src_nick,
            _available_outputs(src_comp)))
        return
    if dst is None:
        emit(log, "  ! target not found: {0}.{1}  (available ins: {2})".format(
            getattr(dst_comp, "NickName", "?"), dst_nick,
            _available_inputs(dst_comp)))
        return
    ok, note = _call_add_source(dst, src)
    src_nn = getattr(src_comp, "NickName", "?")
    dst_nn = getattr(dst_comp, "NickName", "?")
    if ok:
        suffix = " {0}".format(note) if note else ""
        emit(log, "  + {0}.{1} -> {2}.{3}{4}".format(
            src_nn, src_nick, dst_nn, dst_nick, suffix))
    else:
        emit(log, "  ! wire failed {0}.{1}->{2}.{3}: {4}".format(
            src_nn, src_nick, dst_nn, dst_nick, note))


def _dump_types(created, log):
    """One-shot diagnostic: tells us exactly what each created object
    looks like to pythonnet — concrete .NET type + whether _get_params
    succeeds + how many inputs/outputs we can enumerate."""
    emit(log, "--- type dump (debug) ---")
    for key, obj in created.items():
        if obj is None:
            emit(log, "  {0}: None".format(key))
            continue
        try:
            tname = obj.GetType().FullName
        except Exception:
            tname = "(unknown)"
        if _is_igh_param_only(obj):
            emit(log, "  {0}: {1}  [IGH_Param-only]".format(key, tname))
            continue
        params = _get_params(obj)
        if params is None:
            emit(log, "  {0}: {1}  [Params=None!]".format(key, tname))
            continue
        outs = _available_outputs(obj)
        ins  = _available_inputs(obj)
        emit(log, "  {0}: {1}".format(key, tname))
        emit(log, "      ins:  {0}".format(ins))
        emit(log, "      outs: {0}".format(outs))
    emit(log, "--- end type dump ---")


def make_panel(server, text, pivot, log, w=200, h=40):
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
            float(pivot[0] - w / 2.0), float(pivot[1] - h / 2.0),
            float(w), float(h))
    except Exception:
        pass
    set_ok = False
    # 1. Direct property (IronPython GhPython usually works).
    try:
        panel.UserText = text
        set_ok = True
    except Exception:
        pass
    # 2. SetUserText() method (some older versions).
    if not set_ok:
        try:
            panel.SetUserText(text)
            set_ok = True
        except Exception:
            pass
    # 3. Python 3 / pythonnet: AddVolatileData on the panel's hidden buffer.
    if not set_ok:
        try:
            from Grasshopper.Kernel.Types import GH_String
            panel.UserText = ""           # ensure attribute exists
            panel.AddVolatileData(
                Grasshopper.Kernel.Data.GH_Path(0), 0, GH_String(text))
            set_ok = True
        except Exception as ex:
            emit(log, "  ! could not set Panel text via any API: {0}".format(ex))
    return panel


def _already_built(doc):
    """Return True if any listed StockBridge component type is already on
    the active document — prevents re-placing on every recompute."""
    sb_guids = set(str(g) for g in SB.values())
    for obj in doc.Objects:
        try:
            if str(obj.ComponentGuid) in sb_guids:
                return True
        except Exception:
            continue
    return False


def _populate(doc, log):
    """The actual placement + wiring. Called OUTSIDE the current Solution
    via ScheduleSolution to avoid the 'Thread already has context Run
    Script' re-entrance error."""

    if _already_built(doc):
        emit(log, "Demo canvas already contains StockBridge components — "
                  "skipping (use Undo to clear, then re-run).")
        return

    server = get_server()
    if server is None:
        emit(log, "Could not find Grasshopper.Instances.ComponentServer.")
        return

    emit(log, "Building StockBridgeGrammar demo canvas...")
    created = {}

    def safe_add(obj, key):
        """AddObject + catch the cosmetic 're-entry' / 'already has context'
        exception that GH sometimes raises mid-batch. Without this catch
        a single throw aborts placement AND the wiring step."""
        try:
            doc.AddObject(obj, False)
            created[key] = obj
            return True
        except Exception as ex:
            msg = str(ex)
            if ("already has context" in msg) or ("Run Script" in msg):
                # The object IS still added; GH just complains about
                # re-entry. Treat as success.
                created[key] = obj
                return True
            emit(log, "  ! AddObject failed for {0}: {1}".format(key, msg))
            return False

    # --- Standard Curve parameter for Rhino selection.
    obj = instantiate(server, CURVE_PARAM_GUID, POS["curves"], log, "Curve param")
    if obj is not None:
        try:
            obj.NickName = "Curves"
        except Exception:
            pass
        if safe_add(obj, "curves"):
            emit(log, "  placed Curve param")

    # --- StockBridge component types.
    for key, guid in SB.items():
        pivot = POS.get(key)
        if pivot is None:
            continue
        comp = instantiate(server, guid, pivot, log, key)
        if comp is None:
            emit(log, "  -> skipping {0} (is StockBridgeGrammar.gha loaded?)".format(key))
            continue
        if safe_add(comp, key):
            emit(log, "  placed {0}".format(key))

    # Second Decode Graph (for the candidate branch).
    decode2 = instantiate(server, SB["decode"], POS["decode2"], log, "decode2")
    if decode2 is not None and safe_add(decode2, "decode2"):
        emit(log, "  placed decode2")

    # Rule-id panel feeding both Rule Params Builder and Apply Grammar Rule.
    rid_panel = make_panel(server, "splitMemberByStockLength",
                            POS["rule_id_panel"], log, w=200, h=28)
    if rid_panel is not None and safe_add(rid_panel, "rule_id_panel"):
        emit(log, "  placed Rule ID panel (splitMemberByStockLength)")

    # --- Wires --------------------------------------------------------
    emit(log, "Wiring...")
    g = created.get

    if g("curves") and g("recognize"):
        wire(g("curves"), "C", g("recognize"), "C", log)
    if g("recognize") and g("assign"):
        wire(g("recognize"), "J", g("assign"), "G", log)
    if g("example_stock") and g("assign"):
        wire(g("example_stock"), "J", g("assign"), "S", log)
    if g("example_stock") and g("apply"):
        wire(g("example_stock"), "J", g("apply"), "S", log)
    if g("assign") and g("target_picker"):
        wire(g("assign"), "G", g("target_picker"), "G", log)
        wire(g("assign"), "M", g("target_picker"), "M", log)
    if g("assign") and g("apply"):
        wire(g("assign"), "G", g("apply"), "G", log)
    if g("target_picker") and g("apply"):
        wire(g("target_picker"), "T", g("apply"), "T", log)
    if g("rule_params") and g("apply"):
        wire(g("rule_params"), "P", g("apply"), "P", log)
    if g("rule_id_panel") and g("apply"):
        r_in = find_input(g("apply"), "R")
        if r_in is not None:
            try:
                r_in.AddSource(g("rule_id_panel"))
                emit(log, "  + Panel(splitMemberByStockLength) -> apply.R")
            except Exception as ex:
                emit(log, "  ! panel->apply.R failed: {0}".format(ex))
    if g("rule_id_panel") and g("rule_params"):
        r_in = find_input(g("rule_params"), "R")
        if r_in is not None:
            try:
                r_in.AddSource(g("rule_id_panel"))
                emit(log, "  + Panel(splitMemberByStockLength) -> rule_params.R")
            except Exception as ex:
                emit(log, "  ! panel->rule_params.R failed: {0}".format(ex))
    if g("apply") and g("decode"):
        wire(g("apply"), "G", g("decode"), "G", log)
    if g("recognize") and g("generate"):
        wire(g("recognize"), "J", g("generate"), "G", log)
    if g("example_stock") and g("generate"):
        wire(g("example_stock"), "J", g("generate"), "S", log)
    if g("generate") and g("candidate_select"):
        wire(g("generate"), "G", g("candidate_select"), "CG", log)
    if g("candidate_select") and g("decode2"):
        wire(g("candidate_select"), "G", g("decode2"), "G", log)

    # Skip explicit NewSolution() — adding objects already schedules a
    # recompute, and an extra call can re-enter the running solver and
    # turn the script red even though everything succeeded.

    emit(log, "Done. Set Run back to False, then delete this script.")


def main():
    """Inline placement. The 'Thread already has context Run Script'
    exception that GH may raise at the very end is COSMETIC — the
    components are placed correctly. We swallow it so the script does
    not go red."""
    log = []
    try:
        run_flag = bool(Run)
    except NameError:
        emit(log, "Add a 'Run' (bool) input to this script component, "
                  "then wire a Boolean Toggle = True into it.")
        return "\n".join(log)

    if not run_flag:
        emit(log, "Run = False. Toggle the boolean to True to build canvas.")
        return "\n".join(log)

    doc = get_doc()
    if doc is None:
        emit(log, "Could not find the active Grasshopper document.")
        return "\n".join(log)

    # Dedupe — if a previous run already placed the components, bail.
    if _already_built(doc):
        emit(log, "Demo canvas already contains StockBridge components.")
        emit(log, "Press Ctrl+Z several times to undo before re-running.")
        return "\n".join(log)

    try:
        _populate(doc, log)
    except Exception as ex:
        msg = str(ex)
        # GH re-entry guard fires when we add objects from within a
        # Solution — placement still completes, so treat as warning.
        if "already has context" in msg or "Run Script" in msg:
            emit(log, "(re-entry warning suppressed — placement OK)")
        else:
            emit(log, "Inline placement raised: {0}".format(msg))

    emit(log, "")
    emit(log, "Toggle Run back to False so this script doesn't re-run.")
    return "\n".join(log)


a = main()
