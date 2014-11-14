﻿using System.Runtime.InteropServices;

namespace Rubberduck.Reflection.VBA
{
    [ComVisible(false)]
    public class Identifier
    {
        public Identifier(string scope, string name, string typeName)
        {
            _scope = scope;
            _name = name;
            _typeName = typeName;
        }

        private readonly string _scope;
        public string Scope { get { return _scope; } }

        private readonly string _name;
        public string Name { get { return _name; } }

        private readonly string _typeName;
        public string TypeName { get { return _typeName; } }
    }
}
