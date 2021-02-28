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
        public int splitterSlot;
        public Pose slotPose;
        public bool isOutput;
        public bool isBelt;
        public int beltSlot;
        public EntityData entityData;
    }
}
