﻿using System;

namespace Microsoft.Automata.Grammars
{
    /// <summary>
    /// terminal symbol
    /// </summary>
    public class Terminal<T> : GrammarSymbol
    {
        /// <summary>
        /// the term
        /// </summary>
        public T term;
        string name;

        /// <summary>
        /// constructs a terminal
        /// </summary>
        /// <param name="term">given term</param>
        /// <param name="name">given name</param>
        public Terminal(T term, string name)
        {
            this.term = term;
            this.name = name;
        }

        /// <summary>
        /// name of the terminal
        /// </summary>
        public override string Name
        {
            get { return name; }
        }

        /// <summary></summary>
        public override string ToString()
        {
            return name;
        }

        public override bool Equals(object obj)
        {
            var t = obj as Terminal<T>;
            return t != null && base.Equals(obj) && object.Equals(term, t.term);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode() ^ term.GetHashCode();
        }
    }
}
