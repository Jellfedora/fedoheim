using System;
using System.Collections.Generic;
using System.IO;

namespace FedoDeath
{
    // Encodeur GIF89a minimal, sans dépendance externe (palette fixe 256 couleurs + LZW).
    // Volontairement simple : suffisant pour un gif "mémo de mort", pas pour de l'image de précision.
    public static class GifBuilder
    {
        private const int PaletteSize = 256;
        private const int ClearCode = PaletteSize;
        private const int EndCode = PaletteSize + 1;
        private const int MinCodeSize = 8;

        public static byte[] Build(List<byte[]> rawFrames, int width, int height, int frameDelayCentiseconds)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            WriteHeader(w);
            WriteLogicalScreenDescriptor(w, width, height);
            WriteGlobalColorTable(w);
            WriteApplicationExtension(w);

            foreach (var frame in rawFrames)
            {
                var indices = QuantizeFrame(frame, width, height);
                WriteGraphicControlExtension(w, frameDelayCentiseconds);
                WriteImageDescriptor(w, width, height);
                WriteImageData(w, indices);
            }

            w.Write((byte)0x3B); // trailer
            return ms.ToArray();
        }

        private static void WriteHeader(BinaryWriter w)
        {
            w.Write(new[] { 'G', 'I', 'F', '8', '9', 'a' });
        }

        private static void WriteLogicalScreenDescriptor(BinaryWriter w, int width, int height)
        {
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write((byte)0xF7); // GCT présente, résolution couleur 8 bits, non triée, taille GCT = 256
            w.Write((byte)0);    // couleur de fond
            w.Write((byte)0);    // pixel aspect ratio
        }

        private static void WriteGlobalColorTable(BinaryWriter w)
        {
            for (int i = 0; i < PaletteSize; i++)
            {
                var (r, g, b) = PaletteColor(i);
                w.Write(r);
                w.Write(g);
                w.Write(b);
            }
        }

        private static void WriteApplicationExtension(BinaryWriter w)
        {
            w.Write((byte)0x21); // extension introducer
            w.Write((byte)0xFF); // application extension label
            w.Write((byte)11);
            w.Write(new[] { 'N', 'E', 'T', 'S', 'C', 'A', 'P', 'E', '2', '.', '0' });
            w.Write((byte)3);
            w.Write((byte)1);
            w.Write((ushort)0); // boucle infinie
            w.Write((byte)0);
        }

        private static void WriteGraphicControlExtension(BinaryWriter w, int delayCentiseconds)
        {
            w.Write((byte)0x21);
            w.Write((byte)0xF9);
            w.Write((byte)4);
            w.Write((byte)0x04); // disposal = ne pas restaurer, pas de transparence
            w.Write((ushort)delayCentiseconds);
            w.Write((byte)0);
            w.Write((byte)0);
        }

        private static void WriteImageDescriptor(BinaryWriter w, int width, int height)
        {
            w.Write((byte)0x2C);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write((byte)0); // pas de table de couleurs locale
        }

        private static void WriteImageData(BinaryWriter w, byte[] indices)
        {
            w.Write((byte)MinCodeSize);

            var compressed = LzwEncode(indices);

            int offset = 0;
            while (offset < compressed.Count)
            {
                int chunkSize = Math.Min(255, compressed.Count - offset);
                w.Write((byte)chunkSize);
                w.Write(compressed.GetRange(offset, chunkSize).ToArray());
                offset += chunkSize;
            }
            w.Write((byte)0);
        }

        // Matrice de Bayer 4x4 : dithering ordonné pour atténuer le banding de la palette réduite.
        private static readonly int[] BayerMatrix4x4 =
        {
            0, 8, 2, 10,
            12, 4, 14, 6,
            3, 11, 1, 9,
            15, 7, 13, 5
        };

        private static byte[] QuantizeFrame(byte[] rgba, int width, int height)
        {
            var indices = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                // Unity/OpenGL: les données de texture sont rangées du bas vers le haut,
                // alors que le GIF attend ses lignes du haut vers le bas -> on inverse ici.
                int srcRow = height - 1 - y;
                int srcRowOffset = srcRow * width * 4;
                int dstRowOffset = y * width;

                for (int x = 0; x < width; x++)
                {
                    int o = srcRowOffset + x * 4;
                    int threshold = BayerMatrix4x4[(y & 3) * 4 + (x & 3)];
                    indices[dstRowOffset + x] = PaletteIndex(rgba[o], rgba[o + 1], rgba[o + 2], threshold);
                }
            }
            return indices;
        }

        private static byte PaletteIndex(byte r, byte g, byte b, int ditherThreshold)
        {
            int rd = Dither(r, 32, ditherThreshold); // 3 bits -> 8 niveaux, pas de 32
            int gd = Dither(g, 32, ditherThreshold); // 3 bits -> 8 niveaux, pas de 32
            int bd = Dither(b, 64, ditherThreshold); // 2 bits -> 4 niveaux, pas de 64

            int ri = rd >> 5;
            int gi = gd >> 5;
            int bi = bd >> 6;
            return (byte)((ri << 5) | (gi << 2) | bi);
        }

        private static int Dither(byte value, int step, int threshold)
        {
            int bias = (threshold * step / 16) - (step / 2);
            int v = value + bias;
            return v < 0 ? 0 : (v > 255 ? 255 : v);
        }

        private static (byte r, byte g, byte b) PaletteColor(int index)
        {
            int ri = (index >> 5) & 0x7;
            int gi = (index >> 2) & 0x7;
            int bi = index & 0x3;
            return ((byte)(ri * 255 / 7), (byte)(gi * 255 / 7), (byte)(bi * 255 / 3));
        }

        // LZW à largeur de code variable, tel que défini par le format GIF (dictionnaire (préfixe, octet) -> code).
        private static List<byte> LzwEncode(byte[] indices)
        {
            var dict = new Dictionary<(int prefix, byte next), int>();
            int nextCode = EndCode + 1;
            int codeSize = MinCodeSize + 1;

            void ResetDict()
            {
                dict.Clear();
                nextCode = EndCode + 1;
                codeSize = MinCodeSize + 1;
            }

            var output = new List<byte>();
            int bitBuffer = 0;
            int bitCount = 0;

            void EmitCode(int code)
            {
                bitBuffer |= code << bitCount;
                bitCount += codeSize;
                while (bitCount >= 8)
                {
                    output.Add((byte)(bitBuffer & 0xFF));
                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            }

            EmitCode(ClearCode);

            int currentCode = -1;
            foreach (byte index in indices)
            {
                if (currentCode == -1)
                {
                    currentCode = index;
                    continue;
                }

                if (dict.TryGetValue((currentCode, index), out int existing))
                {
                    currentCode = existing;
                    continue;
                }

                EmitCode(currentCode);

                if (nextCode < 4096)
                {
                    dict[(currentCode, index)] = nextCode++;
                    if (nextCode > (1 << codeSize) && codeSize < 12)
                    {
                        codeSize++;
                    }
                }
                else
                {
                    EmitCode(ClearCode);
                    ResetDict();
                }

                currentCode = index;
            }

            if (currentCode != -1)
            {
                EmitCode(currentCode);
            }
            EmitCode(EndCode);

            if (bitCount > 0)
            {
                output.Add((byte)(bitBuffer & 0xFF));
            }

            return output;
        }
    }
}
