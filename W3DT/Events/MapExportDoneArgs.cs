﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace W3DT.Events
{
    public class MapExportDoneArgs : EventArgs
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }

        public MapExportDoneArgs(bool success, string error = null)
        {
            Success = success;
            Message = error;
        }
    }
}
