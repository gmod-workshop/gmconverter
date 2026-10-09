using JeremyAnsel.Xwa.Opt;

#pragma warning disable CS8602, CS8604 // JeremyAnsel.Xwa.Opt exposes populated collections without nullable annotations.

namespace GMConverter.XWingAlliance.Importers;

internal static class FaceTriangulator
{
    public static IEnumerable<TriangleIndices> Triangulate(Face face)
    {
        int[] vertices =
        [
            face.VerticesIndex.A,
            face.VerticesIndex.B,
            face.VerticesIndex.C,
            face.VerticesIndex.D
        ];
        int[] textureCoordinates =
        [
            face.TextureCoordinatesIndex.A,
            face.TextureCoordinatesIndex.B,
            face.TextureCoordinatesIndex.C,
            face.TextureCoordinatesIndex.D
        ];
        int[] normals =
        [
            face.VertexNormalsIndex.A,
            face.VertexNormalsIndex.B,
            face.VertexNormalsIndex.C,
            face.VertexNormalsIndex.D
        ];

        yield return new TriangleIndices(
            [vertices[0], vertices[1], vertices[2]],
            [textureCoordinates[0], textureCoordinates[1], textureCoordinates[2]],
            [normals[0], normals[1], normals[2]]);

        if (vertices[3] >= 0)
        {
            yield return new TriangleIndices(
                [vertices[0], vertices[2], vertices[3]],
                [textureCoordinates[0], textureCoordinates[2], textureCoordinates[3]],
                [normals[0], normals[2], normals[3]]);
        }
    }
}
