﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using W3DT.Events;
using W3DT.Formats;
using W3DT.Formats.WDT;
using W3DT.Formats.ADT;
using W3DT._3D;
using SereniaBLPLib;

namespace W3DT.Runners
{
    public class MapExportException : Exception
    {
        public MapExportException(string message) : base(message) { }
    }

    public class RunnerMapExport : RunnerBase
    {
        private string mapName;
        private string fileName;
        private string filePath;

        public RunnerMapExport(string mapName, string fileName)
        {
            this.mapName = mapName;
            this.fileName = fileName;
            this.filePath = Path.GetDirectoryName(fileName);
        }

        public override void Work()
        {
            LogWrite("Beginning export of {0}...", mapName);

            try
            {
                if (!Program.IsCASCReady)
                    throw new MapExportException("CASC file engine is not loaded.");

                string wdtPath = string.Format(@"World\Maps\{0}\{0}.wdt", mapName);
                Program.CASCEngine.SaveFileTo(wdtPath, Constants.TEMP_DIRECTORY);

                WDTFile headerFile = new WDTFile(Path.Combine(Constants.TEMP_DIRECTORY, wdtPath));
                headerFile.parse();

                if (!headerFile.Chunks.Any(c => c.ChunkID == Chunk_MPHD.Magic))
                    throw new MapExportException("Invalid map header (WDT)");

                // ToDo: Check if world WMO exists and load it.

                // Ensure we have a MAIN chunk before trying to process terrain.
                Chunk_MAIN mainChunk = (Chunk_MAIN)headerFile.Chunks.Where(c => c.ChunkID == Chunk_MAIN.Magic).FirstOrDefault();
                if (mainChunk != null)
                {
                    // Pre-calculate UV mapping for terrain.
                    UV[,,] uvMaps = new UV[8,8,5];
                    for (int x = 0; x < 8; x++)
                    {
                        for (int y = 0; y < 8; y++)
                        {
                            float uTL = 1 - (0.125f * x);
                            float vTL = 1 - (0.125f * y);
                            float uBR = uTL - 0.125f;
                            float vBR = vTL - 0.125f;

                            int aX = 7 - x;
                            uvMaps[aX, y, 0] = new UV(uBR, vTL); // TL
                            uvMaps[aX, y, 1] = new UV(uBR, vBR); // TR
                            uvMaps[aX, y, 2] = new UV(uTL, vTL); // BL
                            uvMaps[aX, y, 3] = new UV(uTL, vBR); // BR
                            uvMaps[aX, y, 4] = new UV(uTL - 0.0625f, vTL - 0.0625f);
                        }
                    }

                    TextureBox texProvider = new TextureBox();
                    WaveFrontWriter ob = new WaveFrontWriter(fileName, texProvider, true, true);

                    for (int y = 0; y < 64; y++)
                    {
                        for (int x = 0; x < 64; x++)
                        {
                            if (mainChunk.map[x, y])
                            {
                                string pathBase = string.Format(@"World\Maps\{0}\{0}_{1}_{2}", mapName, x, y);
                                string adtPath = pathBase + ".adt";
                                string texPath = pathBase + "_tex0.adt";
                                string objPath = pathBase + "_obj0.adt";

                                Program.CASCEngine.SaveFileTo(adtPath, Constants.TEMP_DIRECTORY);
                                Program.CASCEngine.SaveFileTo(texPath, Constants.TEMP_DIRECTORY);
                                Program.CASCEngine.SaveFileTo(objPath, Constants.TEMP_DIRECTORY);

                                string adtTempPath = Path.Combine(Constants.TEMP_DIRECTORY, adtPath);
                                string texTempPath = Path.Combine(Constants.TEMP_DIRECTORY, texPath);
                                string objTempPath = Path.Combine(Constants.TEMP_DIRECTORY, objPath);

                                Dictionary<uint, uint> texMap = new Dictionary<uint, uint>();

                                try
                                {
                                    ADTFile adt = new ADTFile(adtTempPath, ADTFileType.ROOT);
                                    adt.parse();

                                    ADTFile tex = new ADTFile(texTempPath, ADTFileType.TEX);
                                    tex.parse();

                                    ADTFile obj = new ADTFile(objTempPath, ADTFileType.OBJ);
                                    obj.parse();

                                    // Textures
                                    Chunk_MTEX texChunk = (Chunk_MTEX)tex.getChunk(Chunk_MTEX.Magic);
                                    foreach (KeyValuePair<int, string> texture in texChunk.textures.raw())
                                    {
                                        string texFile = texture.Value;

                                        // Register texture in the texture provider
                                        texProvider.addTexture(texture.Key, texFile);
                                        texMap.Add((uint)texture.Key, (uint)texProvider.LastIndex);

                                        // Export raw BLP and convert to PNG.
                                        string dumpPath = Path.Combine(filePath, Path.GetFileNameWithoutExtension(texFile) + ".png");
                                        if (!File.Exists(dumpPath))
                                        {
                                            string tempPath = Path.Combine(Constants.TEMP_DIRECTORY, texFile);

                                            if (!File.Exists(tempPath))
                                                Program.CASCEngine.SaveFileTo(texFile, Constants.TEMP_DIRECTORY);

                                            using (BlpFile blp = new BlpFile(File.OpenRead(tempPath)))
                                                blp.GetBitmap(0).Save(dumpPath);
                                        }
                                    }

                                    //IEnumerable<Chunk_MCNK> rawSoupChunks = adt.getChunksByID(Chunk_MCNK.Magic);
                                    Chunk_MCNK[] soupChunks = adt.getChunksByID(Chunk_MCNK.Magic).Cast<Chunk_MCNK>().ToArray();
                                    Chunk_MCNK[] layerChunks = tex.getChunksByID(Chunk_MCNK.Magic).Cast<Chunk_MCNK>().ToArray();

                                    // Terrain
                                    for (int i = 0; i < 256; i++)
                                    {
                                        Chunk_MCNK soupChunk = soupChunks[i];
                                        Chunk_MCNK layerChunk = layerChunks[i];

                                        // Terrain chunks
                                        Chunk_MCVT hmChunk = (Chunk_MCVT)soupChunk.getChunk(Chunk_MCVT.Magic);
                                        Chunk_MCNR nChunk = (Chunk_MCNR)soupChunk.getChunk(Chunk_MCNR.Magic);

                                        // Texture chunks
                                        Chunk_MCLY layers = (Chunk_MCLY)layerChunk.getChunk(Chunk_MCLY.Magic);

                                        Mesh mesh = new Mesh();
                                        int v = 0;

                                        float pX = soupChunk.position.X;
                                        float pY = soupChunk.position.Y;
                                        float pZ = soupChunk.position.Z;

                                        int ofs = 10;
                                        for (int sX = 8; sX > 0; sX--)
                                        {
                                            for (int sY = 8; sY > 0; sY--)
                                            {
                                                int cIndex = ofs - 1;
                                                int blIndex = cIndex - 9;
                                                int tlIndex = cIndex + 8;

                                                float tr = hmChunk.vertices[tlIndex];
                                                float tl = hmChunk.vertices[tlIndex + 1];
                                                float br = hmChunk.vertices[blIndex];
                                                float bl = hmChunk.vertices[blIndex + 1];
                                                float c = hmChunk.vertices[cIndex];

                                                float oX = pX + (sX * ADTFile.TILE_SIZE);
                                                float oY = pY + (sY * ADTFile.TILE_SIZE);

                                                // Apply UV mapping
                                                for (int uv = 0; uv < 5; uv++)
                                                    mesh.addUV(uvMaps[sX - 1, sY - 1, uv]);

                                                // Apply verts
                                                mesh.addVert(new Position(oX, tl + pZ, oY));
                                                mesh.addVert(new Position(oX, tr + pZ, oY + ADTFile.TILE_SIZE));
                                                mesh.addVert(new Position(oX + ADTFile.TILE_SIZE, bl + pZ, oY));
                                                mesh.addVert(new Position(oX + ADTFile.TILE_SIZE, br + pZ, oY + ADTFile.TILE_SIZE));
                                                mesh.addVert(new Position(oX + (ADTFile.TILE_SIZE / 2), c + pZ, oY + (ADTFile.TILE_SIZE / 2)));

                                                // Normals
                                                mesh.addNormal(nChunk.normals[tlIndex + 1]);
                                                mesh.addNormal(nChunk.normals[tlIndex]);
                                                mesh.addNormal(nChunk.normals[blIndex + 1]);
                                                mesh.addNormal(nChunk.normals[tlIndex]);
                                                mesh.addNormal(nChunk.normals[cIndex]);

                                                uint texID = 0;
                                                if (layers.layers.Length > 0)
                                                {
                                                    uint mapKey = layers.layers[0].textureID;
                                                    if (texMap.ContainsKey(mapKey))
                                                        texID = texMap[mapKey];
                                                }

                                                // Faces
                                                mesh.addFace(texID, v, v + 2, v + 4);
                                                mesh.addFace(texID, v + 1, v + 3, v + 4);
                                                mesh.addFace(texID, v, v + 1, v + 4);
                                                mesh.addFace(texID, v + 2, v + 3, v + 4);

                                                v += 5;
                                                ofs += 1;
                                            }
                                            ofs += 9;
                                        }

                                        ob.addMesh(mesh);
                                    }
                                }
                                catch (ADTException ex)
                                {
                                    LogWrite("Unable to process tile {0},{1} due to exception: {2}", x, y, ex.Message);
                                    LogWrite(ex.StackTrace);
                                }
                            }
                        }
                    }

                    ob.Write();
                    ob.Close();
                }

                // Job's done.
                EventManager.Trigger_MapExportDone(true);
            }
            catch (Exception e)
            {
                EventManager.Trigger_MapExportDone(false, e.Message + e.StackTrace);
            }
        }

        private void LogWrite(string message, params object[] data)
        {
            Log.Write("RunnerMapExport: " + message, data);
        }
    }
}
