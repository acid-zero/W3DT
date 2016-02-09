﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using W3DT.Formats.ADT;

namespace W3DT.Formats
{
    public enum ADTFileType
    {
        ROOT,
        TEX,
        OBJ,
        LOD
    }

    public class ADTException : Exception
    {
        public ADTException(string message) : base(message) { }
    }

    public class ADTFile : ChunkedFormatBase
    {
        public ADTFileType Type { get; private set; }
        private Chunk_MCNK CurrentSub;

        public ADTFile(string file, ADTFileType type) : base(file)
        {
            Type = type;
        }

        public override void storeChunk(Chunk_Base chunk)
        {
            if (chunk is IChunkSoup)
            {
                if (CurrentSub != null)
                    CurrentSub.addChunk(chunk);
                else
                    Log.Write("ADT: Sub-chunk found before MCNK chunk? Shun the demons!");
            }
            else
            {
                Chunks.Add(chunk);

                if (chunk is Chunk_MCNK)
                    CurrentSub = (Chunk_MCNK)chunk;
            }
        }

        public override Chunk_Base lookupChunk(UInt32 magic)
        {
            switch (magic)
            {
                case Chunk_MVER.Magic: return new Chunk_MVER(this);
                case Chunk_MCNK.Magic: return new Chunk_MCNK(this);
                case Chunk_MTEX.Magic: return new Chunk_MTEX(this);
                default: return new Chunk_Base(this);
            }
        }

        public override string getFormatName()
        {
            return "ADT";
        }
    }
}
