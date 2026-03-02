using System;
using System.Collections.Generic;

namespace EMI.SyncInterface
{
    internal struct InterfaceTypes
    {
        public Type Client;
        public List<FieldList> ClientFields;
        public List<MarkeredMethod> ClientMethods;
        public Type Server;
        public List<FieldList> ServerFields;
        public List<MarkeredMethod> ServerMethods;

        public InterfaceTypes(Type client, List<FieldList> cf, List<MarkeredMethod> cm, Type server, List<FieldList> sf, List<MarkeredMethod> sm)
        {
            Client = client;
            ClientFields = cf;
            ClientMethods = cm;
            Server = server;
            ServerFields = sf;
            ServerMethods = sm;
        }
    }
}