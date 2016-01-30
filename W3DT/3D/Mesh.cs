﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpGL;

namespace W3DT._3D
{
    public class Mesh : _3DObject
    {
        private List<Position> verts;
        private List<UV> uvs;
        private List<Face> faces;

        public int VertCount { get { return verts.Count; } }
        public int FaceCount { get { return faces.Count; } }
        public int UVCount { get { return uvs.Count; } }

        public Mesh()
        {
            verts = new List<Position>();
            faces = new List<Face>();
            uvs = new List<UV>();
        }

        public void addVert(Position vert)
        {
            verts.Add(vert);
        }

        public void addUV(UV uv)
        {
            uvs.Add(uv);
        }

        public void addFace(uint texID, params int[] points)
        {
            Face face = new Face(texID);

            foreach (int point in points)
                face.addPoint(verts[point], uvs[point]);

            if (face.PointCount > 0)
                faces.Add(face);
        }

        public override void Draw(OpenGL gl)
        {
            gl.Color(1, 0, 0);
            foreach (Face face in faces)
                face.Draw(gl);
        }
    }
}
