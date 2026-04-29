using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bluscream.MenuManager
{
    [Serializable]
    public class MenuExportData
    {
        public List<MenuExportNode> rootNodes = new List<MenuExportNode>();
        public List<MenuMoveOperation> moveOperations = new List<MenuMoveOperation>();
    }

    [Serializable]
    public class MenuExportNode
    {
        public string name;
        public string originalPath;
        public int type;
        public string parameter;
        public float value;
        public string iconGuid;
        
        public List<MenuExportNode> children = new List<MenuExportNode>();
    }

    [Serializable]
    public class MenuMoveOperation
    {
        public string fromPath;
        public string toPath;
    }
}
