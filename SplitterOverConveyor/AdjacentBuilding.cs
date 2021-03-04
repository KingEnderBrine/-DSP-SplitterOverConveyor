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
        public EntityData entityData;
    }
}
