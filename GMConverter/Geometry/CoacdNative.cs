using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Geometry;

internal static partial class CoacdNative
{
    public static IReadOnlyList<Mesh> Decompose(Mesh inputMesh, CoacdDecompositionOptions options)
    {
        var vertices = inputMesh.Vertices;
        var triangles = inputMesh.Triangles.ToArray();

        if (vertices.Count == 0 || triangles.Length == 0)
        {
            return [];
        }

        var nativeVertices = new double[vertices.Count * 3];
        var nativeTriangles = new int[triangles.Length * 3];

        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i].Position;
            var offset = i * 3;
            nativeVertices[offset + 0] = vertex.X;
            nativeVertices[offset + 1] = vertex.Y;
            nativeVertices[offset + 2] = vertex.Z;
        }

        for (var i = 0; i < triangles.Length; i++)
        {
            var triangle = triangles[i];
            var offset = i * 3;
            nativeTriangles[offset + 0] = triangle.A;
            nativeTriangles[offset + 1] = triangle.B;
            nativeTriangles[offset + 2] = triangle.C;
        }

        var verticesHandle = GCHandle.Alloc(nativeVertices, GCHandleType.Pinned);
        var trianglesHandle = GCHandle.Alloc(nativeTriangles, GCHandleType.Pinned);

        try
        {
            var nativeMesh = new CoacdMesh
            {
                VerticesPtr = verticesHandle.AddrOfPinnedObject(),
                VerticesCount = checked((ulong)vertices.Count),
                TrianglesPtr = trianglesHandle.AddrOfPinnedObject(),
                TrianglesCount = checked((ulong)triangles.Length)
            };

            SetLogLevel("error");
            var result = Run(
                ref nativeMesh,
                options.Threshold,
                options.MaxConvexPieces,
                preprocessMode: 0,
                preprocessResolution: 50,
                resolution: 2000,
                mctsNodes: 20,
                mctsIterations: 150,
                mctsMaxDepth: 3,
                pca: false,
                merge: true,
                decimate: false,
                maxHullVertices: options.MaxHullVertices,
                extrude: false,
                extrudeMargin: 0.01,
                approximationMode: 0,
                seed: 0,
                realMetric: false);

            try
            {
                return ConvertResult(result);
            }
            finally
            {
                FreeMeshArray(result);
            }
        }
        catch (DllNotFoundException ex)
        {
            throw new GMConverterException($"CoACD native library was not found. Build the project with DownloadCoacdNative enabled so lib_coacd is copied next to the executable. {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new GMConverterException($"CoACD native library does not expose the expected API. {ex.Message}");
        }
        finally
        {
            verticesHandle.Free();
            trianglesHandle.Free();
        }
    }

    private static Mesh[] ConvertResult(CoacdMeshArray result)
    {
        var meshCount = checked((int)result.MeshesCount);
        var meshes = new Mesh[meshCount];

        for (var i = 0; i < meshCount; i++)
        {
            var meshPtr = IntPtr.Add(result.MeshesPtr, i * Marshal.SizeOf<CoacdMesh>());
            var mesh = Marshal.PtrToStructure<CoacdMesh>(meshPtr);

            var vertexCount = checked((int)mesh.VerticesCount);
            var triangleCount = checked((int)mesh.TrianglesCount);
            var rawVertices = new double[vertexCount * 3];
            var rawTriangles = new int[triangleCount * 3];

            Marshal.Copy(mesh.VerticesPtr, rawVertices, 0, rawVertices.Length);
            Marshal.Copy(mesh.TrianglesPtr, rawTriangles, 0, rawTriangles.Length);

            var vertices = new Vertex[vertexCount];
            var triangles = new Triangle[triangleCount];

            for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                var offset = vertexIndex * 3;
                vertices[vertexIndex] = new Vertex(
                    new Vector3(
                        (float)rawVertices[offset + 0],
                        (float)rawVertices[offset + 1],
                        (float)rawVertices[offset + 2]),
                    Vector3.UnitZ,
                    Vector2.Zero);
            }

            for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                var offset = triangleIndex * 3;
                triangles[triangleIndex] = new Triangle(
                    rawTriangles[offset + 0],
                    rawTriangles[offset + 1],
                    rawTriangles[offset + 2]);
            }

            meshes[i] = new Mesh(vertices, [new Submesh(null, triangles)]);
        }

        return meshes;
    }

    private static void SetLogLevel(string level)
    {
        var bytes = Encoding.UTF8.GetBytes(level + '\0');
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            CoACD_setLogLevel(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    [LibraryImport("lib_coacd")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void CoACD_setLogLevel(IntPtr level);

    [LibraryImport("lib_coacd", EntryPoint = "CoACD_freeMeshArray")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void FreeMeshArray(CoacdMeshArray meshArray);

    [LibraryImport("lib_coacd", EntryPoint = "CoACD_run")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial CoacdMeshArray Run(
        ref CoacdMesh mesh,
        double threshold,
        int maxConvexPieces,
        int preprocessMode,
        int preprocessResolution,
        int resolution,
        int mctsNodes,
        int mctsIterations,
        int mctsMaxDepth,
        [MarshalAs(UnmanagedType.I1)] bool pca,
        [MarshalAs(UnmanagedType.I1)] bool merge,
        [MarshalAs(UnmanagedType.I1)] bool decimate,
        int maxHullVertices,
        [MarshalAs(UnmanagedType.I1)] bool extrude,
        double extrudeMargin,
        int approximationMode,
        uint seed,
        [MarshalAs(UnmanagedType.I1)] bool realMetric);

    [StructLayout(LayoutKind.Sequential)]
    private struct CoacdMesh
    {
        public IntPtr VerticesPtr;
        public ulong VerticesCount;
        public IntPtr TrianglesPtr;
        public ulong TrianglesCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CoacdMeshArray
    {
        public IntPtr MeshesPtr;
        public ulong MeshesCount;
    }
}

internal sealed record CoacdDecompositionOptions(
    double Threshold,
    int MaxConvexPieces,
    int MaxHullVertices);
