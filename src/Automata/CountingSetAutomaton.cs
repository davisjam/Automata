﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Automata.BooleanAlgebras;

namespace Microsoft.Automata
{
    /// <summary>
    /// Counting-set Automaton
    /// </summary>
    public class CsAutomaton<S> : Automaton<CsLabel<S>>
    {
        PowerSetStateBuilder stateBuilder;
        Dictionary<int, ICounter> countingStates;
        HashSet<int> origFinalStates;

        CsAlgebra<S> productAlgebra;

        public CsAlgebra<S> ProductAlgebra
        {
            get
            {
                return productAlgebra;
            }
        }

        Dictionary<int, HashSet<int>> activeCounterMap;

        HashSet<int> finalCounterSet;

        ICounter[] counters;

        public CsAutomaton(IBooleanAlgebra<S> inputAlgebra, Automaton<CsLabel<S>> aut, PowerSetStateBuilder stateBuilder, Dictionary<int, ICounter> countingStates, HashSet<int> origFinalStates) : base(aut)
        {
            this.stateBuilder = stateBuilder;
            this.countingStates = countingStates;
            this.productAlgebra = new CsAlgebra<S>(inputAlgebra, countingStates.Count);
            this.origFinalStates = origFinalStates;
            activeCounterMap = new Dictionary<int, HashSet<int>>();
            finalCounterSet = new HashSet<int>();
            counters = new ICounter[countingStates.Count];
            foreach (var q in aut.States)
            {
                var q_set = new HashSet<int>();
                activeCounterMap[q] = q_set;
                foreach (var mem in stateBuilder.GetMembers(q))
                {
                    if (countingStates.ContainsKey(mem))
                    {
                        var counterId = countingStates[mem].CounterId;
                        q_set.Add(counterId);
                        if (origFinalStates.Contains(mem))
                            finalCounterSet.Add(counterId);
                        counters[counterId] = countingStates[mem];
                    }
                }
            }
        }

        int GetOriginalInitialState()
        {
            foreach (var q0 in stateBuilder.GetMembers(InitialState))
                return q0;
            throw new AutomataException(AutomataExceptionKind.SetIsEmpty);
        }

        bool __hidePowersets = false;
        public void ShowGraph(string name = "CsAutomaton", bool hidePowersets = false)
        {
            __hidePowersets = hidePowersets;
            base.ShowGraph(name);
        }

        /// <summary>
        /// Describe the state information, including original states if determinized, as well as counters.
        /// </summary>
        public override string DescribeState(int state)
        {
            string s = state.ToString();
            var mems = new List<int>(stateBuilder.GetMembers(state));
            mems.Sort();
            if (!__hidePowersets)
            {
                s += "&#13;{";
                foreach (var q in mems)
                {
                    if (!s.EndsWith("{"))
                        s += ",";
                    s += q.ToString();
                }
                s += "}";
            }
            var state_counters = GetCountersOfState(state);
            var state_counters_list = new List<int>(state_counters);
            state_counters_list.Sort();
            foreach (var c in state_counters_list)
            {
                s += "&#13;";
                if (finalCounterSet.Contains(c))
                    s += "(F)";
                s += "c" + c + ":[" + counters[c].LowerBound + "," + counters[c].UpperBound + "]";
            }
            return s;
        }

        /// <summary>
        /// Describe if the initial state is associuated wit a counter, if so then set it to {0}
        /// </summary>
        /// <returns></returns>
        public override string DescribeStartLabel()
        {
            var initcounters = activeCounterMap[InitialState].GetEnumerator();
            if (initcounters.MoveNext())
            {
                var c = initcounters.Current;
                return string.Format("c{0}={{0}}", c);
            }
            else
                return "";
        }

        public static CsAutomaton<S> CreateFrom(CountingAutomaton<S> ca)
        {
            var productmoves = new List<Move<CsPred<S>>>();
            var alg = new CsAlgebra<S>(((CABA<S>)ca.Algebra).builder.solver, ca.NrOfCounters);
            foreach (var move in ca.GetMoves())
            {
                var ccond = alg.TrueCsConditionSeq;
                if (ca.IsCountingState(move.SourceState))
                {
                    var cid = ca.GetCounter(move.SourceState).CounterId;
                    if (move.Label.Item2.First.OperationKind == CounterOp.EXIT ||
                        move.Label.Item2.First.OperationKind == CounterOp.EXIT_SET0 ||
                        move.Label.Item2.First.OperationKind == CounterOp.EXIT_SET1)
                    {
                        ccond = ccond.And(cid, CsCondition.CANEXIT);
                    }
                    else
                    {
                        if (move.Label.Item2.First.OperationKind != CounterOp.INCR)
                            throw new AutomataException(AutomataExceptionKind.InternalError);

                        ccond = ccond.And(cid, CsCondition.CANLOOP);
                    }
                }
                var pmove = Move<CsPred<S>>.Create(move.SourceState, move.TargetState, alg.MkPredicate(move.Label.Item1.Element, ccond));
                productmoves.Add(pmove);
            }
            var prodaut = Automaton<CsPred<S>>.Create(alg, ca.InitialState, ca.GetFinalStates(), productmoves);

            PowerSetStateBuilder sb;
            var det = prodaut.Determinize(out sb);

            var csmoves = new List<Move<CsLabel<S>>>();

            foreach (var dmove in det.GetMoves())
            { 
                foreach (var prodcond in dmove.Label.GetSumOfProducts())
                {
                    var upd = CsUpdateSeq.MkNOOP(ca.NrOfCounters);
                    foreach (var q in sb.GetMembers(dmove.SourceState))
                        upd = upd | ca.GetCounterUpdate(q, prodcond.Item2, prodcond.Item1);
                    var guard = alg.MkPredicate(prodcond.Item2, prodcond.Item1);
                    csmoves.Add(Move<CsLabel<S>>.Create(dmove.SourceState, dmove.TargetState, CsLabel<S>.MkTransitionLabel(guard, upd, ((CABA<S>)ca.Algebra).builder.solver.PrettyPrint)));
                }
            }

            var csa_aut = Automaton<CsLabel<S>>.Create(null, det.InitialState, det.GetFinalStates(), csmoves);

            var fs = new HashSet<int>(ca.GetFinalStates());

            var csa = new CsAutomaton<S>(((CABA<S>)ca.Algebra).builder.solver, csa_aut, sb, ca.countingStates, fs);

            return csa;
        }

        /// <summary>
        /// Get the active counters associated with the given state.
        /// The set is empty if this state is not asscociated with any counters.
        /// </summary>
        public HashSet<int> GetCountersOfState(int state)
        {
            return activeCounterMap[state];
        }

        /// <summary>
        /// Get the total number of counters
        /// </summary>
        public int NrOfCounters
        {
            get
            {
                return counters.Length;
            }
        }

        /// <summary>
        /// Get the counter info associated with the given counter id
        /// </summary>
        /// <param name="counterId">must be a number between 0 and NrOfCounters-1</param>
        /// <returns></returns>
        public ICounter GetCounterInfo(int counterId)
        {
            return counters[counterId];
        }

        /// <summary>
        /// Returns true if the given counter is a final counter, thus, in final state 
        /// contributes to the overall final state condition.
        /// </summary>
        /// <param name="counterId">must be a number between 0 and NrOfCounters-1</param>
        /// <returns></returns>
        public bool IsFinalCounter(int counterId)
        {
            return finalCounterSet.Contains(counterId);
        }
    }

    public class CsLabel<S>
    {
        bool isFinalCondition;
        public readonly CsPred<S> Guard;
        public readonly CsUpdateSeq Updates;
        Func<S, string> InputToString;

        public bool IsFinalCondition
        {
            get { return isFinalCondition; }
        }

        CsLabel(bool isFinalCondition, CsPred<S> guard, CsUpdateSeq updates, Func<S, string> inputToString)
        {
            this.isFinalCondition = isFinalCondition;
            this.Guard = guard;
            this.Updates = updates;
            this.InputToString = inputToString;
        }

        public static CsLabel<S> MkFinalCondition(CsPred<S> guard, Func<S, string> inputToString = null)
        {
            return new CsLabel<S>(true, guard, null, inputToString);
        }

        public static CsLabel<S> MkTransitionLabel(CsPred<S> guard, CsUpdateSeq updates, Func<S, string> inputToString = null)
        {
            return new CsLabel<S>(false, guard, updates, inputToString);
        }

        public override string ToString()
        {
            var cases = new Sequence<Tuple<CsConditionSeq, S>>(Guard.ToArray());
            string cond = "";
            foreach (var psi in cases)
            {
                var pp = Guard.Algebra.LeafAlgebra as ICharAlgebra<S>;
                cond += (pp != null ? pp.PrettyPrint(psi.Item2) : psi.Item2.ToString());
                var countercond = psi.Item1.ToString();
                if (countercond != "TRUE")
                    cond += "/" + countercond;
            }
            if (isFinalCondition)
            {
                return cond;
            }
            else
            {
                var s = cond;
                var upd = DescribeCounterUpdate();
                if (upd != "")
                {
                    s += ":" + upd;
                }
                return s;
            }
        }

        //private string DescribeCounterCondition()
        //{
        //    string s = "";
        //    for (int i = 0; i < Conditions.Length; i++)
        //    {
        //        if (Conditions[i] != CsCondition.EMPTY && Conditions[i] != CsCondition.NONEMPTY)
        //        {
        //            if (s != "")
        //                s += "&";
        //            s += CsCondition_ToString(Conditions[i]) + "(c" + i.ToString() + ")";
        //        }
        //    }
        //    return s;
        //}

        //static string CsCondition_ToString(CsCondition cond)
        //{
        //    switch (cond)
        //    {
        //        case CsCondition.LOW:
        //            return "L";
        //        case CsCondition.MIDDLE:
        //            return "M";
        //        case CsCondition.HIGH:
        //            return "H";
        //        default:
        //            return cond.ToString();
        //    }
        //}

        private string DescribeCounterUpdate()
        {
            string s = "";
            for (int i = 0; i < Updates.Length; i++)
            {
                if (Updates[i] != CsUpdate.NOOP)
                {
                    if (s != "")
                        s += ";";
                    s += Updates[i].ToString() + "(c" + i.ToString() + ")";
                }
            }
            return s;
        }
    }

    public enum CsUpdate
    {
        /// <summary>
        /// No update
        /// </summary>
        NOOP = 0,
        /// <summary>
        /// Insert 0
        /// </summary>
        SET0 = 1,
        /// <summary>
        /// Insert 1
        /// </summary>
        SET1 = 2,
        /// <summary>
        /// Insert 0 and 1, same as SET0|SET1
        /// </summary>
        SET01 = 3,
        /// <summary>
        /// Increment all elements
        /// </summary>
        INCR = 4,
        /// <summary>
        /// Increment all elements and then insert 0, same as INCR|SET0
        /// </summary>
        INCR0 = 5,
        /// <summary>
        /// Increment all elements and then insert 1, same as INCR|SET1
        /// </summary>
        INCR1 = 6,
        /// <summary>
        /// Increment all elements and then insert 0 and 1, same as INCR|SET0|SET1
        /// </summary>
        INCR01 = 7,
    }

    public enum CsCondition
    {
        /// <summary>
        /// Unsatisfiable condition
        /// </summary>
        FALSE = 0,
        /// <summary>
        /// Nonempty and all elements are below lower bound
        /// </summary>
        LOW = 1,
        /// <summary>
        /// Some element is at least lower bound but it is not the only element if it is the upper bound
        /// </summary>
        MIDDLE = 2,
        /// <summary>
        /// The condition when loop increment is possible, same as LOW|MIDDLE
        /// </summary>
        CANLOOP = 3,
        /// <summary>
        /// Singleton set containing the upper bound
        /// </summary>
        HIGH = 4,
        /// <summary>
        /// All elements are below lower bound, or singleton set containing the upper bound, same as LOW|HIGH
        /// </summary>
        LOWorHIGH = 5,
        /// <summary>
        /// The condition when loop exit is possible, same as MIDDLE|HIGH
        /// </summary>
        CANEXIT = 6,
        /// <summary>
        /// Set is nonempty, same as LOW|MIDDLE|HIGH
        /// </summary>
        NONEMPTY = 7,
        /// <summary>
        /// Set is empty
        /// </summary>
        EMPTY = 8,
        /// <summary>
        /// Same as EMPTY|LOW
        /// </summary>
        CANNOTEXIT = 9,
        /// <summary>
        /// Same as EMPTY|MIDDLE
        /// </summary>
        EMPTYorMIDDLE = 10,
        /// <summary>
        /// Same as EMPTY|MIDDLE|LOW
        /// </summary>
        EMPTYorCANLOOP = 11,
        /// <summary>
        /// Same as EMPTY|HIGH
        /// </summary>
        CANNOTLOOP = 12,
        /// <summary>
        /// Same as EMPTY|HIGH|LOW
        /// </summary>
        EMPTYorHIGHorLOW = 13,
        /// <summary>
        /// Same as EMPTY|HIGH|MIDDLE
        /// </summary>
        EMPTYorCANEXIT = 14,
        /// <summary>
        /// Condition that always holds, same as EMPTY|MIDDLE|HIGH|LOW
        /// </summary>
        TRUE = 15,
    }

    public class CsUpdateSeq
    {
        Tuple<int, ulong, ulong, ulong> vals;

        CsUpdateSeq(int count, ulong set0, ulong set1, ulong incr)
        {
            vals = new Tuple<int, ulong, ulong, ulong>(count, set0, set1, incr);
        }

        public static CsUpdateSeq MkNOOP(int count)
        {
            return new CsUpdateSeq(count, 0, 0, 0);
        }

        public int Length
        {
            get { return vals.Item1; }
        }

        public static CsUpdateSeq Mk(params CsUpdate[] vals)
        {
            ulong set0 = 0;
            ulong set1 = 0;
            ulong incr = 0;
            ulong bit = 1;
            for (int i = 0; i < vals.Length; i++)
            {
                if (vals[i].HasFlag(CsUpdate.SET0))
                    set0 = set0 | bit;
                if (vals[i].HasFlag(CsUpdate.SET1))
                    set1 = set1 | bit;
                if (vals[i].HasFlag(CsUpdate.INCR))
                    incr = incr | bit;
                bit = bit << 1;
            }
            return new CsUpdateSeq(vals.Length, set0, set1, incr);
        }

        public static CsUpdateSeq operator |(CsUpdateSeq left, CsUpdateSeq right)
        {
            if (left.vals.Item1 != right.vals.Item1)
                throw new ArgumentException("Incompatible lenghts");

            return new CsUpdateSeq(left.vals.Item1, left.vals.Item2 | right.vals.Item2, left.vals.Item3 | right.vals.Item3, left.vals.Item4 | right.vals.Item4);
        }

        public CsUpdate this[int i]
        {
            get
            {
                ulong bit = ((ulong)1) << i;
                int val = 0;
                if ((vals.Item2 & bit) != 0)
                    val = val | 1;
                if ((vals.Item3 & bit) != 0)
                    val = val | 2;
                if ((vals.Item4 & bit) != 0)
                    val = val | 4;
                CsUpdate res = (CsUpdate)val;
                return res;
            }
        }

        public override bool Equals(object obj)
        {
            return vals == ((CsUpdateSeq)obj).vals;
        }

        public override int GetHashCode()
        {
            return vals.GetHashCode();
        }

        public CsUpdateSeq Or(int i, CsUpdate upd)
        {
            ulong bit = ((ulong)1) << i;
            ulong set0 = vals.Item2;
            ulong set1 = vals.Item3;
            ulong incr = vals.Item4;
            if (upd.HasFlag(CsUpdate.SET0))
                set0 = set0 | bit;
            if (upd.HasFlag(CsUpdate.SET1))
                set1 = set1 | bit;
            if (upd.HasFlag(CsUpdate.INCR))
                incr = incr | bit;
            CsUpdateSeq res = new CsUpdateSeq(vals.Item1, set0, set1, incr);
            return res;
        }

        /// <summary>
        /// Returns true if all counter operations are NOOP
        /// </summary>
        public bool IsNOOP
        {
            get
            {
                return (vals.Item2 == 0 && vals.Item3 == 0 && vals.Item4 == 0);
            }
        }

        public override string ToString()
        {
            string s = "";
            for (int i = 0; i < Length; i++)
            {
                if (this[i] != CsUpdate.NOOP)
                {
                    if (s != "")
                        s += ";";
                    s += string.Format("{0}({1})", this[i], i);
                }
            }
            if (s == "")
                return "NOOP";
            else
                return s;
        }
    }

    public class CsConditionSeq
    {
        bool isAND;
        /// <summary>
        /// Returns true iff this sequence represents a conjunction
        /// </summary>
        public bool IsAND
        {
            get
            {
                return isAND;
            }
        }
        Tuple<int, ulong, ulong, ulong, ulong, ulong> elems;
        /// <summary>
        /// Number of conditions
        /// </summary>
        public int Length { get { return elems.Item1; } }
        internal ulong Mask { get { return elems.Item2; } }
        internal ulong Empty { get { return elems.Item3; } }
        internal ulong Low { get { return elems.Item4; } }
        internal ulong Middle { get { return elems.Item5; } }
        internal ulong High { get { return elems.Item6; } }

        CsConditionSeq(bool isAND, Tuple<int, ulong, ulong, ulong, ulong, ulong> elems)
        {
            this.isAND = isAND;
            this.elems = elems;
        }

        /// <summary>
        /// Make a sequence that corresponds to the conjunction of the individual counter conditions.
        /// </summary>
        /// <param name="vals">i'th element is the i'th counter condition</param>
        public static CsConditionSeq MkAND(params CsCondition[] vals)
        {
            return MkSeq(true, vals);
        }

        /// <summary>
        /// Make a sequence that corresponds to the disjunction of the individual counter conditions.
        /// </summary>
        /// <param name="vals">i'th element is the i'th counter condition</param>
        public static CsConditionSeq MkOR(params CsCondition[] vals)
        {
            return MkSeq(false, vals);
        }

        static CsConditionSeq MkSeq(bool isOr, CsCondition[] vals)
        {
            if (vals.Length > 64)
                throw new NotImplementedException("More than 64 counter support not implemented");

            int length = vals.Length;
            ulong mask = (length == 64 ? ulong.MaxValue : ((ulong)1 << length) - 1);
            ulong empty = 0;
            ulong low = 0;
            ulong middle = 0;
            ulong high = 0;
            ulong bitmask = 1;
            for (int i = 0; i < length; i++)
            {
                CsCondition cond = vals[i];
                if (cond.HasFlag(CsCondition.LOW))
                    low = low | bitmask;
                if (cond.HasFlag(CsCondition.MIDDLE))
                    middle = middle | bitmask;
                if (cond.HasFlag(CsCondition.HIGH))
                    high = high | bitmask;
                if (cond.HasFlag(CsCondition.EMPTY))
                    empty = empty | bitmask;
                bitmask = bitmask << 1;
            }
            var elems = new Tuple<int, ulong, ulong, ulong, ulong, ulong>(length, mask, empty, low, middle, high);
            return new CsConditionSeq(isOr, elems);
        }

        /// <summary>
        /// Creates a conjunction with all individual counter conditions being TRUE
        /// </summary>
        public static CsConditionSeq MkTrue(int length)
        {
            CsCondition[] vals = new CsCondition[length];
            for (int i = 0; i < length; i++)
                vals[i] = CsCondition.TRUE;
            return MkAND(vals);
        }

        /// <summary>
        /// Creates a disjunction with all individual counter conditions being FALSE
        /// </summary>
        public static CsConditionSeq MkFalse(int length)
        {
            CsCondition[] vals = new CsCondition[length];
            for (int i = 0; i < length; i++)
                vals[i] = CsCondition.FALSE;
            return MkOR(vals);
        }

        public override bool Equals(object obj)
        {
            var cond = obj as CsConditionSeq;
            if (cond == null)
                return false;
            else
                return cond.isAND == isAND && elems.Equals(cond.elems);
        }

        public override int GetHashCode()
        {
            return (isAND ? elems.GetHashCode() : elems.GetHashCode() << 1);
        }

        public override string ToString()
        {
            string s = "";
            if (isAND)
            {
                for (int i=0; i < Length; i++)
                {
                    if (this[i] != CsCondition.TRUE)
                    {
                        if (s != "")
                            s += "&";
                        s += string.Format("{0}({1})", this[i], i);
                    }
                }
                if (s == "")
                    s = "TRUE";
            }
            else
            {
                for (int i = 0; i < Length; i++)
                {
                    if (this[i] != CsCondition.FALSE)
                    {
                        if (s != "")
                            s += "|";
                        s += string.Format("{0}({1})", this[i], i);
                    }
                }
                if (s == "")
                    s = "FALSE";
            }
            return s;
        }

        public CsCondition[] ToArray()
        {
            var list = new List<CsCondition>();
            var arr = new CsCondition[Length];
            for (int i = 0; i < Length; i++)
                arr[i] = this[i];
            return arr;
        }

        /// <summary>
        /// Returns the i'th condition
        /// </summary>
        /// <param name="i">must be between 0 and Length-1</param>
        public CsCondition this[int i]
        {
            get
            {
                if (i >= Length || i < 0)
                    throw new ArgumentOutOfRangeException();
                else
                {
                    ulong bitmask = ((ulong)1) << i;
                    int res = 0;
                    if ((Low & bitmask) != 0)
                        res = (int)CsCondition.LOW;
                    if ((Middle & bitmask) != 0)
                        res = res | (int)CsCondition.MIDDLE;
                    if ((High & bitmask) != 0)
                        res = res | (int)CsCondition.HIGH;
                    if ((Empty & bitmask) != 0)
                        res = res | (int)CsCondition.EMPTY;
                    return (CsCondition)res;
                }
            }
        }

        /// <summary>
        /// If conjunction, returns true if all conditions in the sequence are different from FALSE.
        /// If disjunction, returns true if some condition in the sequence is different from FALSE
        /// </summary>
        public bool IsSatisfiable
        {
            get
            {
                if (isAND)
                    return (Empty | Low | Middle | High) == Mask;
                else
                    return (Middle != 0 | Low != 0 | High != 0 | Empty != 0);
            }
        }

        /// <summary>
        /// If conjunction, returns true if all conditions in the sequence are TRUE.
        /// If disjunction, returns true if some condition in the sequence is TRUE.
        /// </summary>
        public bool IsValid
        {
            get
            {
                var mask = Mask;
                if (isAND)
                    return (Empty == mask && Low == mask && Middle == mask && High == Mask);
                else
                    return (mask & (~Empty | ~Low | ~Middle | ~High)) != mask;
            }
        }

        /// <summary>
        /// Create a conjunction sequence of two sequences that represent conjunctions
        /// </summary>
        public static CsConditionSeq operator &(CsConditionSeq left, CsConditionSeq right)
        {
            if (left.Length == right.Length && left.isAND && right.isAND)
            {
                int length = left.Length;
                ulong mask = left.Mask;
                ulong empty = left.Empty & right.Empty;
                ulong low = left.Low & right.Low;
                ulong middle = left.Middle & right.Middle;
                ulong high = left.High & right.High;
                var elems = new Tuple<int, ulong, ulong, ulong, ulong, ulong>(length, mask, empty, low, middle, high);
                var res = new CsConditionSeq(true, elems);
                return res;
            }
            else
                throw new InvalidOperationException("Incompatible arguments, & is only supported between conjunction sequences");
        }

        /// <summary>
        /// Create a disjunction sequence of two sequences that represent disjunctions
        /// </summary>
        public static CsConditionSeq operator |(CsConditionSeq left, CsConditionSeq right)
        {
            if (left.Length == right.Length && !left.isAND && !right.isAND)
            {
                int length = left.Length;
                ulong mask = left.Mask;
                ulong empty = left.Empty | right.Empty;
                ulong low = left.Low | right.Low;
                ulong middle = left.Middle | right.Middle;
                ulong high = left.High | right.High;
                var elems = new Tuple<int, ulong, ulong, ulong, ulong, ulong>(length, mask, empty, low, middle, high);
                var res = new CsConditionSeq(false, elems);
                return res;
            }
            else
                throw new InvalidOperationException("Incompatible arguments, | is only supported between disjunction sequences");
        }

        /// <summary>
        /// Complement the sequence from OR to AND and vice versa, 
        /// individual counter conditions are complemented.
        /// </summary>
        public static CsConditionSeq operator ~(CsConditionSeq arg)
        {
            int length = arg.Length;
            ulong mask = arg.Mask;
            ulong empty = mask & ~arg.Empty;
            ulong low = mask & ~arg.Low;
            ulong middle = mask & ~arg.Middle;
            ulong high = mask & ~arg.High;
            var elems = new Tuple<int, ulong, ulong, ulong, ulong, ulong>(length, mask, empty, low, middle, high);
            var res = new CsConditionSeq(!arg.isAND, elems);
            return res;
        }

        public CsConditionSeq Update(int i, CsCondition cond)
        {
            if (i >= Length)
                throw new ArgumentOutOfRangeException();

            ulong bit = ((ulong)1) << i;
            ulong bitmask = ~bit;
            //clear the bit
            ulong empty = Empty & bitmask;
            ulong low = Low & bitmask;
            ulong mid = Middle & bitmask;
            ulong high = High & bitmask;
            //set the new value
            if (cond.HasFlag(CsCondition.LOW))
                low = low | bit;
            if (cond.HasFlag(CsCondition.MIDDLE))
                mid = mid | bit;
            if (cond.HasFlag(CsCondition.HIGH))
                high = high | bit;
            if (cond.HasFlag(CsCondition.EMPTY))
                empty = empty | bit;

            var elems = new Tuple<int, ulong, ulong, ulong, ulong, ulong>(Length, Mask, empty, low, mid, high);
            return new CsConditionSeq(isAND, elems);
        }

        /// <summary>
        /// Update the i'th element to this[i] | cond
        /// </summary>
        public CsConditionSeq Or(int i, CsCondition cond)
        {
            if (i >= Length)
                throw new ArgumentOutOfRangeException();

            ulong bit = ((ulong)1) << i;
            ulong empty = Empty;
            ulong low = Low;
            ulong mid = Middle;
            ulong high = High;
            //set the new value
            if (cond.HasFlag(CsCondition.LOW))
                low = low | bit;
            if (cond.HasFlag(CsCondition.MIDDLE))
                mid = mid | bit;
            if (cond.HasFlag(CsCondition.HIGH))
                high = high | bit;
            if (cond.HasFlag(CsCondition.EMPTY))
                empty = empty | bit;

            var elems = new Tuple<int, ulong, ulong, ulong, ulong, ulong>(Length, Mask, empty, low, mid, high);
            var res = new CsConditionSeq(isAND, elems);
            return res;
        }

        /// <summary>
        /// Update the i'th element to this[i] & cond
        /// </summary>
        public CsConditionSeq And(int i, CsCondition cond)
        {
            if (i >= Length)
                throw new ArgumentOutOfRangeException();

            ulong bit = ((ulong)1) << i;
            ulong bit_false = ~bit;

            ulong empty = Empty;
            ulong low = Low;
            ulong mid = Middle;
            ulong high = High;
            //set the new value
            if (!cond.HasFlag(CsCondition.LOW))
                low = low & bit_false;
            if (!cond.HasFlag(CsCondition.MIDDLE))
                mid = mid & bit_false;
            if (!cond.HasFlag(CsCondition.HIGH))
                high = high & bit_false;
            if (!cond.HasFlag(CsCondition.EMPTY))
                empty = empty & bit_false;

            var elems = new Tuple<int, ulong, ulong, ulong, ulong, ulong>(Length, Mask, empty, low, mid, high);
            var res = new CsConditionSeq(isAND, elems);
            return res;
        }
    }
}
