"""Pinned, offline OpenCascade STEP -> metre geometry. Run from any directory."""
import argparse
import hashlib
import importlib.metadata
import json
import math
from pathlib import Path

from OCP.BRep import BRep_Tool
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.TopLoc import TopLoc_Location
from OCP.TopAbs import TopAbs_FACE, TopAbs_REVERSED
from OCP.TopoDS import TopoDS
from inspect_step import read, explore

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = Path('unity/Assets/Trainer/Content/Spatial/Generated')
VERSION = '7.9.3.1.1'
SOURCES = {
    'fixture': 'f4532e591b0d7150a2988378a24e992b930580ca06074af60d434a7f6542d04c',
    'welded_part': '647e8ce51ce0f02cda545677b969d484517b526841dc4e88a75a8829103f1117',
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encode(value):
    return (json.dumps(value, indent=2, ensure_ascii=True, allow_nan=False) + '\n').encode()


def vec(values):
    return dict(zip(('x', 'y', 'z'), (0.0 if abs(v) < 1e-12 else round(v, 10) for v in values)))


def pose(destination, source, position=(0, 0, 0), rotation=(0, 0, 0, 1)):
    return dict(destination=destination, source=source, position=vec(position),
                rotation=dict(zip(('x', 'y', 'z', 'w'), rotation)), scale=1.0)


def convert(name):
    source = ROOT / 'engineering/cad/source' / (name + '.step')
    if sha(source.read_bytes()) != SOURCES[name]:
        raise ValueError(f'{name}: source identity mismatch; review and version the source manifest')
    shape = read(source)
    # OCCT loads these explicitly millimetre STEP inputs in mm. This is the sole scale.
    mesh = BRepMesh_IncrementalMesh(shape, 0.05, False, 0.1, False)
    if not mesh.IsDone():
        raise ValueError(f'{name}: tessellation failed')
    surfaces = []
    vertices, indices = [], []
    for face_index, item in enumerate(explore(shape, TopAbs_FACE)):
        face = TopoDS.Face_s(item)
        location = TopLoc_Location()
        tri = BRep_Tool.Triangulation_s(face, location)
        if tri is None:
            raise ValueError(f'{name}: missing triangulation on face {face_index}')
        for i in range(1, tri.NbTriangles() + 1):
            ids = list(tri.Triangle(i).Get())
            if face.Orientation() == TopAbs_REVERSED:
                ids[1], ids[2] = ids[2], ids[1]
            points = [tuple(v * 0.001 for v in tri.Node(j).Transformed(location.Transformation()).Coord()) for j in ids]
            u, v = [tuple(points[k][j] - points[0][j] for j in range(3)) for k in (1, 2)]
            n = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
            length = math.sqrt(sum(x*x for x in n))
            if length < 1e-14:
                raise ValueError(f'{name}: degenerate triangle {face_index}/{i}')
            normal = vec(x/length for x in n)
            surfaces.append(dict(id=f'{name}-face-{face_index:02d}-triangle-{i:04d}',
                                 sourceFace=face_index, a=vec(points[0]), b=vec(points[1]), c=vec(points[2]), normal=normal))
            indices.extend(range(len(vertices), len(vertices)+3))
            vertices.extend(vec(p) for p in points)
    frame = 'Fixture' if name == 'fixture' else 'Workpiece'
    selection = json.loads((ROOT / 'engineering/cad/selections.json').read_text())[name]
    if selection['sourceSha256'] != SOURCES[name] or selection['units'] != 'm':
        raise ValueError(f'{name}: semantic selection source/units mismatch')
    seams = selection['seams']
    for seam in seams:
        arc = [0.0]
        for a, b in zip(seam['points'], seam['points'][1:]):
            arc.append(arc[-1] + math.sqrt(sum((b[k]-a[k])**2 for k in ('x', 'y', 'z'))))
        seam['arcLengthsMetres'] = arc
    geometry = dict(schemaVersion=1, id=name + '-geometry', revision=selection['revision'], frame=frame, units='m',
                    sourceSha256=SOURCES[name], closedSolid=True, surfaces=surfaces, seams=[], cleaningRegions=[],
                    semanticStatus=selection['semanticStatus'], semanticReason=selection['semanticReason'])
    geometry['seams'] = seams
    geometry['cleaningRegions'] = selection['cleaningRegions']
    visual = dict(schemaVersion=1, frame=frame, units='m', vertices=vertices, triangles=indices)
    return {f'{name}.geometry.json': encode(geometry), f'{name}.visual.json': encode(visual)}


def build():
    if importlib.metadata.version('cadquery-ocp-novtk') != VERSION:
        raise ValueError('Converter version mismatch')
    files = {}
    for name in SOURCES:
        files.update(convert(name))
    source_records = [dict(id=name, path=f'engineering/cad/source/{name}.step', sha256=identity,
                           revision=1, units='mm') for name, identity in SOURCES.items()]
    catalog = json.loads((ROOT / 'engineering/cad/authoring.json').read_text())
    catalog['bakeInputs'] = [dict(path=path, sha256=sha((ROOT / path).read_bytes())) for path in (
        'tools/cad/bake.py', 'tools/cad/inspect_step.py', 'tools/cad/requirements.txt',
        'engineering/cad/authoring.json', 'engineering/cad/selections.json')]
    catalog['sources'] = source_records
    catalog['imports'] = [dict(sourceId=name, converter='cadquery-ocp-novtk', converterVersion=VERSION,
        millimetresToMetres=.001, axisMap='x,y,z', pivot='shared CAD origin',
        authoredFromCadMetres=pose(frame, 'CadMetres'), chordToleranceMetres=.00005,
        angularToleranceRadians=.1, geometryFile=f'{name}.geometry.json',
        geometrySha256=sha(files[f'{name}.geometry.json']), visualFile=f'{name}.visual.json',
        visualSha256=sha(files[f'{name}.visual.json']))
        for name, frame in [('fixture', 'Fixture'), ('welded_part', 'Workpiece')]]
    files['supplied.spatial'] = encode(catalog)
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='rebuild in memory and compare every committed output byte')
    args = parser.parse_args()
    files = build()
    for name, data in files.items():
        path = ROOT / OUTPUT / name
        if args.check:
            if not path.exists() or path.read_bytes() != data:
                raise ValueError(f'Reproducibility mismatch: {path.relative_to(ROOT)}')
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        print(f'{name}: {len(data)} bytes SHA256={sha(data)}')


if __name__ == '__main__':
    main()
