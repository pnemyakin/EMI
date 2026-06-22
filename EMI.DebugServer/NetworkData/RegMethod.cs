namespace EMI.DebugServer.NetworkData
{
    public struct RegMethod
    {
        public long ID;
        public string Name;
        public int Count;

        public RegMethod(long iD, string name, int count)
        {
            ID = iD;
            Name = name;
            Count = count;
        }
    }
}
