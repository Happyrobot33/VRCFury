using System.Linq;
using UnityEngine;
using VF.Utils;

namespace VF.Builder {
    public static class MeshBaker {
        /**
         * Returns a baked mesh, where the vertices are in local space in relation to the renderer's transform.
         * This is true even for skinned meshes (where in other places, the offsets are commonly in relation to the root bone)
         */
        public static BakedMesh BakeMesh(Renderer renderer, Transform origin = null, bool applyScale = false) {
            var mesh = renderer.GetMesh();

            if (renderer is SkinnedMeshRenderer skin) {
                var temporaryMesh = new Mesh();
                skin.BakeMesh(temporaryMesh);

                // If the skinned mesh doesn't have any bones attached, it's treated like a regular mesh and BakeMesh applies no transforms
                // So we have to skip rescaling, otherwise we'd apply the inverse scale inappropriately and it would be too small.
                var actuallySkinned = mesh != null && mesh.boneWeights.Length > 0;
                if (actuallySkinned) {
                    var scale = skin.transform.lossyScale;
                    var inverseScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
                    ApplyScale(temporaryMesh, inverseScale);
                }

                mesh = temporaryMesh;
            }

            if (mesh == null) return null;

            Vector3[] vertices;
            Vector3[] normals;
            if (origin) {
                vertices = mesh.vertices
                    .Select(v => origin.InverseTransformPoint(renderer.transform.TransformPoint(v))).ToArray();
                normals = mesh.normals
                    .Select(v => origin.InverseTransformDirection(renderer.transform.TransformDirection(v))).ToArray();
            } else {
                vertices = mesh.vertices;
                normals = mesh.normals;
            }

            if (applyScale && origin != null) {
                ApplyScale(vertices, origin.lossyScale);
            }

            return new BakedMesh() {
                vertices = vertices,
                normals = normals
            };
        }

        private static void ApplyScale(Mesh mesh, Vector3 scale) {
            var verts = mesh.vertices;
            ApplyScale(verts, scale);
            mesh.vertices = verts;
        }
        private static void ApplyScale(Vector3[] verts, Vector3 scale) {
            for (var i = 0; i < verts.Length; i++) {
                verts[i].Scale(scale);
            }
        }

        public class BakedMesh {
            public Vector3[] vertices;
            public Vector3[] normals;
        }
    }
}
