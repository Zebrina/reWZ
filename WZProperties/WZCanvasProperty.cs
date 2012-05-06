﻿// This file is part of reWZ.
// 
// reWZ is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// reWZ is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with reWZ. If not, see <http://www.gnu.org/licenses/>.
// 
// Linking this library statically or dynamically with other modules
// is making a combined work based on this library. Thus, the terms and
// conditions of the GNU General Public License cover the whole combination.
// 
// As a special exception, the copyright holders of this library give you
// permission to link this library with independent modules to produce an
// executable, regardless of the license terms of these independent modules,
// and to copy and distribute the resulting executable under terms of your
// choice, provided that you also meet, for each linked independent module,
// the terms and conditions of the license of that module. An independent
// module is a module which is not derived from or based on this library.
// If you modify this library, you may extend this exception to your version
// of the library, but you are not obligated to do so. If you do not wish to
// do so, delete this exception statement from your version.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace reWZ.WZProperties
{
    /// <summary>
    ///   A bitmap property, containing an image, and children.
    /// </summary>
    public class WZCanvasProperty : WZProperty<Bitmap>
    {
        internal WZCanvasProperty(string name, WZObject parent, WZBinaryReader br, WZImage container)
            : base(name, parent, br, container, true)
        {}

        internal override Bitmap Parse(WZBinaryReader br, bool initial)
        {
            br.Skip(1);
            if (br.ReadByte() == 1) {
                br.Skip(2);
                List<WZObject> l = WZExtendedParser.ParsePropertyList(br, this, Image, Image._encrypted);
                if (ChildCount == 0) l.ForEach(Add);
            }
            int width = br.ReadWZInt(); // width
            int height = br.ReadWZInt(); // height
            int format1 = br.ReadWZInt(); // format 1
            int format2 = br.ReadByte(); // format 2
            br.Skip(4);
            int blockLen = br.ReadInt32();
            if (initial) br.Skip(blockLen); // block Len & png data
            else {
                br.Skip(1);
                // ushort header = br.PeekFor(() => br.ReadUInt16());
                byte[] pngData = br.ReadBytes(blockLen - 1);
                return ParsePNG(width, height, format1 + format2, Image._encrypted ? DecryptPNG(pngData) : pngData);
            }
            return null;
        }

        private byte[] DecryptPNG(byte[] @in)
        {
            using (MemoryStream @sIn = new MemoryStream(@in, false))
            using (BinaryReader @sBr = new BinaryReader(@sIn))
            using (MemoryStream @sOut = new MemoryStream(@in.Length)) {
                while (@sIn.Position < @sIn.Length) {
                    int blockLen = @sBr.ReadInt32();
                    @sOut.Write(File._aes.DecryptBytes(@sBr.ReadBytes(blockLen)), 0, blockLen);
                }
                return @sOut.ToArray();
            }
        }

        private Bitmap ParsePNG(int width, int height, int format, byte[] data)
        {
            byte[] dec;
#if ZLIB
            using (MemoryStream @in = new MemoryStream(data, 0, data.Length))
#else
            using (MemoryStream @in = new MemoryStream(data, 2, data.Length - 2))
#endif
                dec = WZBinaryReader.Inflate(@in);

            switch (format) {
                case 1:
                    Debug.Assert(dec.Length == width*height*2);
                    byte[] argb = new byte[dec.Length*2];
                    for (int i = 0; i < dec.Length; i++) {
                        argb[i*2] = (byte)((dec[i] & 0x0F)*0x11);
                        argb[i*2 + 1] = (byte)(((dec[i] & 0xF0) >> 4)*0x11);
                    }
                    return new Bitmap(width, height, 4*width, PixelFormat.Format32bppArgb, GCHandle.Alloc(argb, GCHandleType.Pinned).AddrOfPinnedObject());
                case 2:
                    Debug.Assert(dec.Length == width*height*4);
                    return new Bitmap(width, height, 4*width, PixelFormat.Format32bppArgb, GCHandle.Alloc(dec, GCHandleType.Pinned).AddrOfPinnedObject());
                case 513:
                    Debug.Assert(dec.Length == width*height*2);
                    return new Bitmap(width, height, dec.Length/height, PixelFormat.Format16bppRgb565, GCHandle.Alloc(dec, GCHandleType.Pinned).AddrOfPinnedObject());
                case 517:
                    Bitmap ret = new Bitmap(width, height);
                    Debug.Assert(dec.Length == width*height/128);
                    int x = 0, y = 0;
                    unchecked {
                        foreach (byte t in dec)
                            for (byte j = 0; j < 8; j++) {
                                byte iB = (byte)(((t & (0x01 << (7 - j))) >> (7 - j))*0xFF);
                                for (int k = 0; k < 16; k++) {
                                    if (x == width) {
                                        x = 0;
                                        y++;
                                    }
                                    ret.SetPixel(x, y, Color.FromArgb(0xFF, iB, iB, iB));
                                    x++;
                                }
                            }
                        return ret;
                    }
                default:
                    return WZFile.Die<Bitmap>(String.Format("Unknown bitmap format {0}.", format));
            }
        }
    }
}