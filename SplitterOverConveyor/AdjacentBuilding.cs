using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SplitterOverConveyor
{
    public struct AdjacentBuilding
    {
        public bool validBelt;
        public int slot;
        public Vector3 slotPos;
        public bool isOutput;
        public bool isBelt;
        public EntityData entityData;
    }
}
