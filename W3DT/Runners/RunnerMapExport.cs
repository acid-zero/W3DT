﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
        private List<Point> includeOnly;

        public RunnerMapExport(string mapName, string fileName, List<Point> includeOnly = null)
        {
            this.mapName = mapName;
            this.fileName = fileName;
            this.filePath = Path.GetDirectoryName(fileName);
            this.includeOnly = includeOnly;
        }

        private bool ShouldInclude(int x, int y)
        {
            if (includeOnly == null || includeOnly.Count == 0)
                return true;

            return includeOnly.Contains(new Point(x, y));
        }

        public override void Work()
        {
            LogWrite("Beginning export of {0}...", mapName);

            TextureBox texProvider = new TextureBox();
            WaveFrontWriter ob = new WaveFrontWriter(fileName, texProvider, true, true, false);

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
                Chunk_MPHD headerChunk = (Chunk_MPHD)headerFile.Chunks.Where(c => c.ChunkID == Chunk_MPHD.Magic).FirstOrDefault();

                if (mainChunk != null && headerChunk != null)
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

                    int meshIndex = 1;

                    // Create a directory for map data (alpha maps, etc).
                    string dataDirRaw = string.Format("{0}.data", mapName);
                    string dataDir = Path.Combine(Path.GetDirectoryName(fileName), dataDirRaw);

                    if (!Directory.Exists(dataDir))
                        Directory.CreateDirectory(dataDir);

                    for (int y = 0; y < 64; y++)
                    {
                        for (int x = 0; x < 64; x++)
                        {
                            if (mainChunk.map[x, y] && ShouldInclude(x, y))
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

                                    uint texIndex = 0;
                                    Bitmap[] textureData = new Bitmap[texChunk.textures.count()];
                                    foreach (KeyValuePair<int, string> texture in texChunk.textures.raw())
                                    {
                                        string texFile = texture.Value;
                                        string tempPath = Path.Combine(Constants.TEMP_DIRECTORY, texFile);

                                        if (!File.Exists(tempPath))
                                            Program.CASCEngine.SaveFileTo(texFile, Constants.TEMP_DIRECTORY);

                                        using (BlpFile blp = new BlpFile(File.OpenRead(tempPath)))
                                            textureData[texIndex] = blp.GetBitmap(0);

                                        texIndex++;
                                    }

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

                                        // Alpha mapping
                                        Chunk_MCAL alphaMapChunk = (Chunk_MCAL)layerChunk.getChunk(Chunk_MCAL.Magic);

                                        string texFileName = string.Format("baked_{0}_{1}_{2}.png", x, y, i);
                                        string texFilePath = Path.Combine(dataDir, texFileName);

                                        if (!File.Exists(texFilePath))
                                        {
                                            Bitmap bmpBase = new Bitmap(textureData[layers.layers[0].textureID]);
                                            using (Graphics baseG = Graphics.FromImage(bmpBase))
                                            using (ImageAttributes att = new ImageAttributes())
                                            {
                                                att.SetWrapMode(WrapMode.TileFlipXY);
                                                baseG.CompositingQuality = CompositingQuality.HighQuality;
                                                baseG.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                                baseG.CompositingMode = CompositingMode.SourceOver;

                                                for (int mI = 1; mI < layers.layers.Length; mI++) // First layer never has an alpha map
                                                {
                                                    byte[,] alphaMap;
                                                    MCLYLayer layer = layers.layers[mI];
                                                    bool headerFlagSet = ((headerChunk.flags & 0x4) == 0x4) || ((headerChunk.flags & 0x80) == 0x80);
                                                    bool layerFlagSet = ((layer.flags & 0x200) == 0x200);
                                                    bool fixAlphaMap = !((soupChunk.flags & 0x200) == 0x200);

                                                    if (layerFlagSet)
                                                        alphaMap = alphaMapChunk.parse(Chunk_MCAL.CompressType.COMPRESSED, layer.ofsMCAL, fixAlphaMap);
                                                    else
                                                        alphaMap = alphaMapChunk.parse(headerFlagSet ? Chunk_MCAL.CompressType.UNCOMPRESSED_4096 : Chunk_MCAL.CompressType.UNCOMPRESSED_2048, layer.ofsMCAL, fixAlphaMap);

                                                    Bitmap bmpRawTex = textureData[layer.textureID];
                                                    Bitmap bmpAlphaMap = new Bitmap(64, 64);
                                                    for (int drawX = 0; drawX < 64; drawX++)
                                                        for (int drawY = 0; drawY < 64; drawY++)
                                                            bmpAlphaMap.SetPixel(drawX, drawY, Color.FromArgb(alphaMap[drawX, drawY], 0, 0, 0));

                                                    Bitmap bmpAlphaMapScaled = new Bitmap(bmpRawTex.Width, bmpRawTex.Height);
                                                    using (Graphics g = Graphics.FromImage(bmpAlphaMapScaled))
                                                    {
                                                        g.CompositingQuality = CompositingQuality.HighQuality;
                                                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                                        g.CompositingMode = CompositingMode.SourceCopy;
                                                        g.DrawImage(bmpAlphaMap, new Rectangle(0, 0, bmpAlphaMapScaled.Width, bmpAlphaMapScaled.Height), 0, 0, bmpAlphaMap.Width, bmpAlphaMap.Height, GraphicsUnit.Pixel, att);
                                                    }
                                                    bmpAlphaMap.Dispose();

                                                    Bitmap bmpTex = new Bitmap(bmpRawTex.Width, bmpRawTex.Height);
                                                    for (int drawX = 0; drawX < bmpRawTex.Width; drawX++)
                                                    {
                                                        for (int drawY = 0; drawY < bmpRawTex.Height; drawY++)
                                                        {
                                                            // Hacky fix to flip the texture.
                                                            // Remove this if we fix the terrain read-order.
                                                            int sourceX = (bmpRawTex.Width - 1) - drawX;
                                                            //int sourceY = (bmpRawTex.Height - 1) - drawY;

                                                            bmpTex.SetPixel(sourceX, drawY, Color.FromArgb(
                                                                bmpAlphaMapScaled.GetPixel(drawX, drawY).A,
                                                                bmpRawTex.GetPixel(drawX, drawY).R,
                                                                bmpRawTex.GetPixel(drawX, drawY).G,
                                                                bmpRawTex.GetPixel(drawX, drawY).B
                                                            ));
                                                        }
                                                    }
                                                    bmpAlphaMapScaled.Dispose();
                                                    baseG.DrawImage(bmpTex, 0, 0, bmpBase.Width, bmpBase.Height);
                                                }

                                                bmpBase.Save(texFilePath);
                                                bmpBase.Dispose();
                                            }
                                        }

                                        texProvider.addTexture(-1, Path.Combine(dataDirRaw, texFileName));

                                        Mesh mesh = new Mesh("Terrain Mesh #" + meshIndex);
                                        meshIndex++;

                                        int v = 0;

                                        float pX = soupChunk.position.X;
                                        float pY = -soupChunk.position.Y;
                                        float pZ = soupChunk.position.Z;

                                        int ofs = 10;
                                        for (int sX = 8; sX > 0; sX--)
                                        {
                                            for (int sY = 1; sY < 9; sY++)
                                            {
                                                int cIndex = ofs - 1;
                                                int blIndex = cIndex - 9;
                                                int tlIndex = cIndex + 8;

                                                float tr = hmChunk.vertices[tlIndex + 1];
                                                float tl = hmChunk.vertices[tlIndex];
                                                float br = hmChunk.vertices[blIndex + 1];
                                                float bl = hmChunk.vertices[blIndex];
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
                                                mesh.addNormal(nChunk.normals[tlIndex]);
                                                mesh.addNormal(nChunk.normals[tlIndex + 1]);
                                                mesh.addNormal(nChunk.normals[blIndex]);
                                                mesh.addNormal(nChunk.normals[blIndex + 1]);
                                                mesh.addNormal(nChunk.normals[cIndex]);

                                                // Faces
                                                uint texFaceIndex = (uint)texProvider.LastIndex;
                                                mesh.addFace(texFaceIndex, v, v + 2, v + 4);
                                                mesh.addFace(texFaceIndex, v + 1, v + 3, v + 4);
                                                mesh.addFace(texFaceIndex, v, v + 1, v + 4);
                                                mesh.addFace(texFaceIndex, v + 2, v + 3, v + 4);

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
                ob.Close();
                EventManager.Trigger_MapExportDone(false, e.Message + e.StackTrace);
            }
        }

        private void LogWrite(string message, params object[] data)
        {
            Log.Write("RunnerMapExport: " + message, data);
        }
    }
}
