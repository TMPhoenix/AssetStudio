﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AssetStudio.Extensions;

namespace AssetStudio
{
    public sealed class BuildSettings : Object
    {
        public string m_Version;

        public BuildSettings(AssetPreloadData preloadData) : base(preloadData)
        {
            int levelsNum = reader.ReadInt32();
            for (int i = 0; i < levelsNum; i++)
            {
                var level = reader.ReadAlignedString();
            }

            var hasRenderTexture = reader.ReadBoolean();
            var hasPROVersion = reader.ReadBoolean();
            var hasPublishingRights = reader.ReadBoolean();
            var hasShadows = reader.ReadBoolean();

            m_Version = reader.ReadAlignedString();
        }
    }
}
