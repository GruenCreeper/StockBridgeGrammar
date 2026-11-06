"""
Create the demonstration truss geometry in Rhino.

Set the Rhino document units to metres and run this file with
_RunPythonScript. The example is a 12 m-span, 1.6 m-deep Warren-style
truss with eight bays in the XZ plane (Y = 0).

Members are placed on four layers:
    SB_demo_bottom_chord
    SB_demo_top_chord
    SB_demo_diagonals
    SB_demo_verticals

Select the curves on these layers using a Grasshopper Curve parameter
(Set Multiple Curves), then connect it to Recognize Structure.C.
Layer names organise the example and are not interpreted as member roles.
"""

import Rhino
import rhinoscriptsyntax as rs
import scriptcontext as sc

# Bridge dimensions ----------------------------------------------------
SPAN   = 12.0   # metres
HEIGHT = 1.6    # metres (truss depth)
BAYS   = 8

LAYERS = {
    "bottom":   ("SB_demo_bottom_chord", (47, 133, 90)),    # green
    "top":      ("SB_demo_top_chord",    (29, 78, 216)),    # blue
    "diagonal": ("SB_demo_diagonals",    (180, 83, 9)),     # amber
    "vertical": ("SB_demo_verticals",    (124, 58, 237)),   # violet
}


def ensure_layer(name, rgb):
    if not rs.IsLayer(name):
        rs.AddLayer(name=name, color=rgb)
    return name


def add_line(p0, p1, layer):
    line_id = rs.AddLine(p0, p1)
    if line_id:
        rs.ObjectLayer(line_id, layer)
    return line_id


def main():
    for kind, (name, rgb) in LAYERS.items():
        ensure_layer(name, rgb)

    dx = SPAN / BAYS

    # Bottom-chord nodes (Y = 0 plane, Z = 0).
    bottom = [(i * dx, 0.0, 0.0) for i in range(BAYS + 1)]
    # Top-chord nodes — shifted half a bay so a Warren pattern emerges.
    top = [(i * dx + dx * 0.5, 0.0, HEIGHT) for i in range(BAYS)]

    layer_b = LAYERS["bottom"][0]
    layer_t = LAYERS["top"][0]
    layer_d = LAYERS["diagonal"][0]
    layer_v = LAYERS["vertical"][0]

    # Bottom + top chord segments.
    for i in range(BAYS):
        add_line(bottom[i], bottom[i + 1], layer_b)
    for i in range(BAYS - 1):
        add_line(top[i], top[i + 1], layer_t)

    # Warren diagonals zig-zagging between bottom and top nodes.
    for i in range(BAYS):
        add_line(bottom[i], top[i],     layer_d)  # /\
        add_line(top[i],     bottom[i + 1], layer_d)  # /\

    # Pratt-style verticals at every interior bottom node so the demo
    # includes all four member roles.
    for i in range(1, BAYS):
        # Closest top node by X.
        closest = min(top, key=lambda p: abs(p[0] - bottom[i][0]))
        add_line(bottom[i], closest, layer_v)

    sc.doc.Views.Redraw()
    print("Created demo truss:")
    print("  span     = {0:.2f} m".format(SPAN))
    print("  height   = {0:.2f} m".format(HEIGHT))
    print("  bays     = {0}".format(BAYS))
    print("  bottom   = {0} segments on {1}".format(BAYS, layer_b))
    print("  top      = {0} segments on {1}".format(BAYS - 1, layer_t))
    print("  diagonal = {0} segments on {1}".format(BAYS * 2, layer_d))
    print("  vertical = {0} segments on {1}".format(BAYS - 1, layer_v))
    print("")
    print("Next step:")
    print("  In Grasshopper, drop a Curve parameter, right-click → Set")
    print("  Multiple Curves, and lasso all curves on the SB_demo_* layers.")
    print("  Wire that into StockBridge Recognize Structure.")


if __name__ == "__main__":
    main()
