﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AssetStudio.Extensions;

namespace AssetStudio
{
    public sealed class PlayerSettings : Object
    {
        public string companyName;
        public string productName;

        public PlayerSettings(AssetPreloadData preloadData) : base(preloadData)
        {
            if (version[0] > 5 || (version[0] == 5 && version[1] >= 4)) //5.4.0 nad up
            {
                var productGUID = reader.ReadBytes(16);
            }

            var AndroidProfiler = reader.ReadBoolean();
            //bool AndroidFilterTouchesWhenObscured 2017.2 and up
            //bool AndroidEnableSustainedPerformanceMode 2018 and up
            reader.AlignStream(4);
            int defaultScreenOrientation = reader.ReadInt32();
            int targetDevice = reader.ReadInt32();
            if (version[0] < 5 || (version[0] == 5 && version[1] < 3)) //5.3 down
            {
                if (version[0] < 5) //5.0 down
                {
                    int targetPlatform = reader.ReadInt32(); //4.0 and up targetGlesGraphics
                    if (version[0] > 4 || (version[0] == 4 && version[1] >= 6)) //4.6 and up
                    {
                        var targetIOSGraphics = reader.ReadInt32();
                    }
                }
                int targetResolution = reader.ReadInt32();
            }
            else
            {
                var useOnDemandResources = reader.ReadBoolean();
                reader.AlignStream(4);
            }
            if (version[0] > 3 || (version[0] == 3 && version[1] >= 5)) //3.5 and up
            {
                var accelerometerFrequency = reader.ReadInt32();
            }
            companyName = reader.ReadAlignedString();
            productName = reader.ReadAlignedString();
        }
    }
}
