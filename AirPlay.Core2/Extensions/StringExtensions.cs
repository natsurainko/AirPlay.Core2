using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AirPlay.Core2.Extensions;

internal static class StringExtensions
{
    extension(string value)
    {
        public byte[] HexToBytes()
        {
            byte[] data = new byte[value.Length / 2];
            for (int index = 0; index < data.Length; index++)
            {
                string byteValue = value.Substring(index * 2, 2);
                data[index] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return data;
        }
    }
}
