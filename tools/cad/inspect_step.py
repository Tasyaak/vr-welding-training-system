"""Local B-rep inspection; no geometric or welding semantics are inferred."""
from pathlib import Path
import re
from OCP.STEPControl import STEPControl_Reader
from OCP.IFSelect import IFSelect_RetDone
from OCP.TopExp import TopExp_Explorer
from OCP.TopAbs import TopAbs_FACE, TopAbs_SOLID, TopAbs_REVERSED
from OCP.TopoDS import TopoDS
from OCP.BRepAdaptor import BRepAdaptor_Surface
from OCP.BRepCheck import BRepCheck_Analyzer
from OCP.BRepBndLib import BRepBndLib
from OCP.Bnd import Bnd_Box


def read(path):
    text = Path(path).read_bytes().decode('latin1')
    if not re.search(r'SI_UNIT\s*\(\s*\.MILLI\.\s*,\s*\.METRE\.\s*\)', text):
        raise ValueError(f"Expected explicit millimetre STEP source: {path}")
    reader = STEPControl_Reader()
    if reader.ReadFile(str(path)) != IFSelect_RetDone:
        raise ValueError(f"Cannot import {path}")
    reader.TransferRoots()
    shape = reader.OneShape()
    if not BRepCheck_Analyzer(shape).IsValid():
        raise ValueError(f"Invalid B-rep: {path}")
    solids = list(explore(shape, TopAbs_SOLID))
    if len(solids) != 1:
        raise ValueError(f"Expected one solid: {path}: {len(solids)}")
    return shape


def explore(shape, kind):
    iterator = TopExp_Explorer(shape, kind)
    while iterator.More():
        yield iterator.Current()
        iterator.Next()


if __name__ == "__main__":
    for path in sorted(Path("engineering/cad/source").glob("*.step")):
        shape = read(path)
        print(path.name)
        for index, item in enumerate(explore(shape, TopAbs_FACE)):
            face = TopoDS.Face_s(item)
            surface = BRepAdaptor_Surface(face)
            box = Bnd_Box()
            BRepBndLib.AddOptimal_s(face, box, False, False)
            normal = None
            if "Plane" in str(surface.GetType()):
                direction = surface.Plane().Axis().Direction()
                sign = -1 if face.Orientation() == TopAbs_REVERSED else 1
                normal = [round(sign * v, 7) for v in direction.Coord()]
            print(index, str(surface.GetType()), [round(v, 7) for v in box.Get()], normal)
