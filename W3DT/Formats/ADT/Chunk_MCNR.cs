﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using W3DT._3D;

namespace W3DT.Formats.ADT
{
    public class Chunk_MCNR : Chunk_Base, IChunkSoup
    {
        // MCNR ADT Chunk
        // Normals

        public const UInt32 Magic = 0x4D434E52;
        public Position[] normals { get; private set; }

        public Chunk_MCNR(ADTFile file) : base(file, "MCNR", Magic)
        {
            normals = new Position[145];
            for (int i = 0; i < 145; i++)
                normals[i] = new Position(file.readUInt8() / 127, file.readUInt8() / 127, file.readUInt8() / 127);
        }
    }
}
