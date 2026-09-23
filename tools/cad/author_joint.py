"""Bake an owner-selected finite T joint; never classify arbitrary edges as welds."""
import copy
import math

from OCP.BRep import BRep_Tool
from OCP.BRepAdaptor import BRepAdaptor_Curve
from OCP.GeomAbs import GeomAbs_Line
from OCP.TopAbs import TopAbs_EDGE, TopAbs_FACE, TopAbs_VERTEX
from OCP.TopoDS import TopoDS
from inspect_step import explore

KEYS = ('x', 'y', 'z')


def point(values):
    return dict(zip(KEYS, values))


def area(t):
    u = [t[1][k] - t[0][k] for k in KEYS]
    v = [t[2][k] - t[0][k] for k in KEYS]
    return math.sqrt(sum(n*n for n in (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]))) / 2


def contains(t, p):
    a, b, c = [t[k] for k in ('a', 'b', 'c')]
    return abs(sum(area(tri) for tri in ([p, b, c], [a, p, c], [a, b, p])) - area([a, b, c])) < 1e-10


def clip(polygon, axis, bound, sign):
    result = []
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        da, db = sign*(a[axis]-bound), sign*(b[axis]-bound)
        if da >= 0:
            result.append(a)
        if (da >= 0) != (db >= 0):
            ratio = da/(da-db)
            p = {k: a[k] + ratio*(b[k]-a[k]) for k in KEYS}
            p[axis] = bound
            result.append(p)
    return result


def author(shape, surfaces, selection):
    spec = selection['joint']
    if spec['direction'] != '+X' or spec['cleaningWidthMetres'] <= 0:
        raise ValueError('Joint requires explicit +X direction and positive cleaning width')
    faces = list(explore(shape, TopAbs_FACE))
    first, second = spec['sourceFaces']
    common = []
    for edge in explore(faces[first], TopAbs_EDGE):
        if any(edge.IsSame(other) for other in explore(faces[second], TopAbs_EDGE)):
            if not any(edge.IsSame(other) for other in common):
                common.append(edge)
    if len(common) != 1 or BRepAdaptor_Curve(TopoDS.Edge_s(common[0])).GetType() != GeomAbs_Line:
        raise ValueError('Selected faces must share exactly one straight B-rep edge')
    vertices = []
    for vertex in explore(common[0], TopAbs_VERTEX):
        p = point(v * .001 for v in BRep_Tool.Pnt_s(TopoDS.Vertex_s(vertex)).Coord())
        if p not in vertices:
            vertices.append(p)
    vertices.sort(key=lambda p: p['x'])
    if len(vertices) != 2 or vertices[1]['x'] <= vertices[0]['x']:
        raise ValueError('Selected seam must have two distinct directed endpoints')
    if any(abs(p['y']-spec['yMetres']) > 1e-10 or abs(p['z']-spec['zMetres']) > 1e-10 for p in vertices):
        raise ValueError('Selected B-rep edge is not on the owner-selected Y/Z planes')
    supports = []
    for face, expected in zip((first, second), ((0, 1, 0), (0, 0, 1))):
        triangles = [s for s in surfaces if s['sourceFace'] == face]
        if not triangles or any(sum((s['normal'][k]-n)**2 for k, n in zip(KEYS, expected)) > 1e-12 for s in triangles):
            raise ValueError('Joint must be on the visible +Y/+Z faces')
        candidates = [s for s in triangles if all(contains(s, p) for p in vertices)]
        if len(candidates) != 1:
            raise ValueError('Seam needs splitting at changed tessellation support boundaries')
        supports.append(candidates[0]['id'])
    length = vertices[1]['x'] - vertices[0]['x']
    evidence = spec['authoringEvidence']
    seam = dict(id=spec['id'], authoringEvidence=evidence, points=vertices,
                surfaceIds=[supports[0]], adjacentSurfaceIds=[supports[1]], arcLengthsMetres=[0.0, length])
    regions = []
    for phase in ('PreWeld', 'PostWeld'):
        masks, ids = [], []
        for face, axis, origin in ((first, 'z', spec['zMetres']), (second, 'y', spec['yMetres'])):
            total_area = 0.0
            for s in surfaces:
                if s['sourceFace'] != face:
                    continue
                polygon = [s[k] for k in ('a', 'b', 'c')]
                for key, bound, sign in ((axis, origin, 1), (axis, origin+spec['cleaningWidthMetres'], -1),
                                         ('x', vertices[0]['x'], 1), ('x', vertices[1]['x'], -1)):
                    polygon = clip(polygon, key, bound, sign)
                for i in range(1, len(polygon)-1):
                    tri = [polygon[0], polygon[i], polygon[i+1]]
                    size = area(tri)
                    if size < 1e-12:
                        continue
                    total_area += size
                    masks.append(dict(id=f'{spec["id"]}-{phase}-{len(masks)+1}', sourceFace=face,
                                      a=tri[0], b=tri[1], c=tri[2], normal=s['normal']))
                    ids.append(s['id'])
            if abs(total_area-length*spec['cleaningWidthMetres']) > 1e-10:
                raise ValueError('Finite selected face does not cover full 15 mm cleaning band')
        regions.append(dict(id=f'{spec["id"]}-{phase}', phase=phase, authoringEvidence=evidence,
                            surfaceIds=ids, triangles=copy.deepcopy(masks)))
    return [seam], regions
