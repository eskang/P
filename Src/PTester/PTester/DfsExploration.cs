﻿#define __VISIBLE_ABSTRACTION__
#define __FILE_DUMP__
// #define __STATE_INVARIANTS__
// #define __TRANS_INVARIANTS__

using System;
using System.Collections.Generic;
using System.IO;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using P.Runtime;
using System.Diagnostics;

namespace P.Tester
{
    static class DfsExploration
    {
        private static int max_queue_size;

        public static bool UseDepthBounding = false;
        public static int DepthBound = 100;

        public static bool UseStateHashing = true; // currently doesn't make sense without

        public static HashSet<   int   > visited = new HashSet<int>();
        public static HashSet<StateImpl> visible = new HashSet<StateImpl>(new StateImplComparer());

        public static int size_Visited_previous = 0;
        public static int size_Visible_previous = 0;
        public static int size_Visible_previous_previous = 0;

        public static void Dfs(StateImpl start)
        {
            if (!UseStateHashing) throw new NotImplementedException();

#if ! __VISIBLE_ABSTRACTION__
            Console.WriteLine("Warning: visible-state abstraction turned off; some diagnostic output about visible states is meaningless");
#endif

            int k = PrtEventBuffer.k; // const ref would be better
            Console.WriteLine("Using queue bound of {0}", k);

            visited.Clear();
            visible.Clear();

#if DEBUG
            max_queue_size = 0;
#endif

            var stack = new Stack<BacktrackingState>();

#if __FILE_DUMP__
            StreamWriter visited_k = new StreamWriter("visited-" + ( k < 10 ? "0" : "" ) + k.ToString() + ".txt"); // for dumping visited states as strings into a file
#endif

            StateImpl start_k = (StateImpl) start.Clone(); // we need a fresh clone in each iteration (k) of Dfs

            stack.Push(new BacktrackingState(start_k));
            int start_hash = start_k.GetHashCode();
            visited.Add(start_hash);

#if __FILE_DUMP__
            visited_k.Write(start_k.ToPrettyString()); // + " = " + start_hash.ToPrettyString());
            visited_k.WriteLine("==================================================");
#endif

            StateImpl vstart_k = (StateImpl) start_k.Clone(); vstart_k.abstract_me();
            visible.Add(vstart_k);

            
            // DFS begin
            while (stack.Count != 0)
            {
                var bstate = stack.Pop();

                // PrintStackDepth(stack.Count);

                if (bstate.CurrIndex >= bstate.State.EnabledMachines.Count) // if "done" with bstate
                {
                    continue;
                }

                BacktrackingState next = Execute(bstate);    // execute the enabled machine pointed to by currIndex. Also, advance currIndex and/or choiceIndex
                stack.Push(bstate);                          // after increasing currIndex/choiceIndex, push state back on. This is like modifying bstate "on the stack"

                if (!CheckFailure(next.State, next.depth))   // check for failure before adding new state: may fail due to failed assume, in which case we don't want to add
                {
                    // update visited state hashset
                    var hash = next.State.GetHashCode();
                    if (!visited.Add(hash))
                        continue;

#if __VISIBLE_ABSTRACTION__
                    // update visible state set
                    StateImpl next_vs = (StateImpl)next.State.Clone(); next_vs.abstract_me();
                    visible.Add(next_vs);
#endif

                    stack.Push(next);

#if __FILE_DUMP__
                    visited_k.Write(next.State.ToPrettyString()); // + " = " + hash.ToPrettyString());
                    visited_k.WriteLine("==================================================");
#endif

#if DEBUG
                    // diagnostics

                    // Print number of states explored
                    if (visited.Count % 100 == 0)
                    {
                        Console.WriteLine("-------------- Number of states visited so far = {0}", visited.Count);
                    }

                    // update maximum encountered queue size
                    foreach (PrtImplMachine m in next.State.ImplMachines)
                    {
                        int m_size = m.eventQueue.Size();
                        if (m_size > max_queue_size)
                        {
                            max_queue_size = m_size;
                            Console.WriteLine("New maximum queue size observed  = {0}", max_queue_size);
                        }
                    }
#endif
                }
            }

            Console.WriteLine("");

            Console.WriteLine("Number of global  states visited = {0}", visited.Count);
            Console.WriteLine("Number of visible states visited = {0}", visible.Count);

#if __FILE_DUMP__
            visited_k.Close();

#if  __VISIBLE_ABSTRACTION__
            // dump reached visible states into a file
            StreamWriter visible_k = new StreamWriter("visible-" + ( k < 10 ? "0" : "" ) + k.ToString() +  ".txt");
            foreach (StateImpl vs in visible)
            {
                visible_k.Write(vs.ToPrettyString());
                visible_k.WriteLine("==================================================");
            }

            visible_k.Close();
#endif
            
#endif
        }

#if false
        // Empty-tail abstraction:
        // Step II: compute visible successors, and return true ("converged") iff all of them are already contained in visible:
        static bool visible_converged_empty_tail()
        {
            Debug.Assert(visible.Count > 0);
            foreach (StateImpl vs in visible)
            {
                // iterate through immediate successors
                for (int currIndex = 0; currIndex < vs.ImplMachines.Count; ++currIndex)
                {
                    PrtImplMachine m = vs.ImplMachines[currIndex];

                    // reject disabled machines
                    if (!(m.currentStatus == PrtMachineStatus.Enabled))
                        continue;

                    // reject machines not dequeing or receiving. I assume these are the only two that can lead to a call to PrtDequeueEvent
                    if (!(m.nextSMOperation == PrtNextStatemachineOperation.DequeueOperation ||
                          m.nextSMOperation == PrtNextStatemachineOperation.ReceiveOperation))
                        continue;

                    if (m.eventQueue.Empty()) // apparently enabled machines whose next SM op is dequeue or receive may have still an empty queue
                        continue;

                    StateImpl vs_succ;
                    PrtImplMachine m_succ;

                    // try to dequeue head event
                    vs_succ = (StateImpl)vs.Clone();
                    m_succ = vs_succ.ImplMachines[currIndex];
                    Debug.Assert(m_succ.eventQueue.Size() == 1);
                    string m_succ_head_str = m_succ.eventQueue.head().ToString(); // for diagnostics only
                    m_succ.PrtRunStateMachine();
                    if (m_succ.eventQueue.Empty() && !CheckFailure(vs_succ, 0)) // if dequeueing head was successful
                    {
                        // choice 1: concrete tail is empty
                        if (new_cand(vs, vs_succ, currIndex, "head event " + m_succ_head_str))
                            return false;
                        // choice 2: concrete tail is non-empty. Put *anything* machine m_succ receives into the head of the queue
                        foreach (PrtEventNode ev in PrtEventBuffer.Messages)
                        {
                            StateImpl vs_succ_cand = (StateImpl)vs_succ.Clone();
                            PrtImplMachine m_succ_cand = vs_succ_cand.ImplMachines[currIndex];
                            m_succ_cand.eventQueue.make_head(ev);
                            if (new_cand(vs, vs_succ_cand, currIndex, "head event " + m_succ_head_str))
                                return false;
                        }
                    }
                    else
                    {
                        // if dequeing the head was not successful, try to dequeue any tail event. Since we don't know what they are, we must try all
                        foreach (PrtEventNode ev in PrtEventBuffer.Messages)
                        {
                            StateImpl vs_succ_cand = (StateImpl)vs.Clone();
                            PrtImplMachine m_succ_cand = vs_succ_cand.ImplMachines[currIndex];
                            m_succ_cand.eventQueue.make_head(ev);
                            Debug.Assert(m_succ_cand.eventQueue.Size() == 1);
                            m_succ_cand.PrtRunStateMachine();
                            if (m_succ_cand.eventQueue.Empty() && !CheckFailure(vs_succ_cand, 0)) // dequeuing ev was successful. All fields are now correctly set, but we need to adjust the event queue
                            {
                                m_succ_cand.eventQueue.make_head(m.eventQueue.head()); // restore original head
                                if (new_cand(vs, vs_succ_cand, currIndex, "tail event " + ev.ToString()))
                                    return false;
                            }
                        }
                    }
                }
            }
            return true;
        }
#endif

        // Tail-list abstraction:
        // Step II: compute visible successors, and return true ("converged") iff all of them are already contained in visible:
        static bool visible_converged()
        {
            foreach (StateImpl vs in visible)
            {
                // iterate through immediate successors
                for (int currIndex = 0; currIndex < vs.ImplMachines.Count; ++currIndex)
                {
                    PrtImplMachine m = vs.ImplMachines[currIndex];
                    PrtEventBuffer m_q = m.eventQueue;

                    // reject disabled machines
                    if (!(m.currentStatus == PrtMachineStatus.Enabled))
                        continue;

                    // reject machines not dequeing or receiving. I assume these are the only two that can lead to a call to PrtDequeueEvent
                    if (!(m.nextSMOperation == PrtNextStatemachineOperation.DequeueOperation ||
                          m.nextSMOperation == PrtNextStatemachineOperation.ReceiveOperation))
                        continue;

                    if (m.eventQueue.Empty()) // apparently enabled machines whose next SM op is dequeue or receive may have still an empty queue
                        continue;

                    // try to dequeue an event
                    StateImpl vs_succ = (StateImpl)vs.Clone();
                    PrtImplMachine m_succ = vs_succ.ImplMachines[currIndex];
                    PrtEventBuffer m_succ_q = m_succ.eventQueue;
                    m_succ.PrtRunStateMachine();
                    if (m_succ_q.Size() < m_q.Size() && !CheckFailure(vs_succ, 0)) // if dequeing was successful
                    {
                        Debug.Assert(m_q.Size() == m_succ_q.Size() + 1); // we have dequeued exactly one event. Now we have to find it, to correct the abstract queue
                        int i;
                        for (i = 0; i < m_q.Size(); ++i)
                            if (i == m_succ_q.Size() || m_q.events[i] != m_succ_q.events[i])
                                break;
                        Debug.Assert(i < m_q.Size());
                        PrtEventNode dequeued_ev = m_q.events[i];
                        Debug.Assert(! m_succ_q.events.Contains(dequeued_ev)); // we just dequeued it!
                        // choice 1: dequeued_ev existed exactly once in m_q. Then it is gone after the dequeue, in both abstract and concrete. The new state is valid.
                        if (new_cand(vs, vs_succ, currIndex, dequeued_ev))
                            return false;
                        // choice 2: dequeued_ev existed >= twice in m_q. Now we need to find the positions where to re-introduce in the abstract, and try them all non-deterministically
                        for (int j = i; j < m_succ_q.Size(); ++j)
                        {
                            // insert dequeue_ev at position j
                            m_succ_q.events.Insert(j, dequeued_ev); // restore previous state
                            if (new_cand(vs, vs_succ, currIndex, dequeued_ev))
                                return false;
                            m_succ_q.events.RemoveAt(j); // restore previous state
                        }
                        m_succ_q.events.Add(dequeued_ev);
                        if (new_cand(vs, vs_succ, currIndex, dequeued_ev))
                            return false;
                    }
                }
            }
            return true;
        }

        static bool new_cand(StateImpl vs, StateImpl vs_succ, int currIndex, PrtEventNode ev)
        {
            if (visible.Contains(vs_succ)
#if __STATE_INVARIANTS__
                || !vs_succ.state_invariant(currIndex)
#endif
#if __TRANS_INVARIANTS__
                || !vs_succ.trans_invariant(currIndex, vs)
#endif
                )
                return false;

            // candidate is new and satisfies the invariants

            Console.WriteLine("did not converge.");
            Console.WriteLine("Found a so-far unreached successor candidate. It was generated by ImplMachine {0} while trying to dequeue event {1}.", currIndex, ev.ToString());

            StreamWriter vs_SW      = new StreamWriter("vs.txt");      vs_SW     .WriteLine(vs     .ToPrettyString()); vs_SW     .Close();
            StreamWriter vs_succ_SW = new StreamWriter("vs_succ.txt"); vs_succ_SW.WriteLine(vs_succ.ToPrettyString()); vs_succ_SW.Close();

            Console.WriteLine("Dumped abstract source state and successor candidate state into files. Their hashes are {0} and {1}.", vs.GetHashCode(), vs_succ.GetHashCode());
            Console.WriteLine("In source state, ImplMachine {0}: queue should be non-empty.", currIndex);
            Console.WriteLine("In successor candidate state, ImplMachine {0}: if tail was empty, its queue should now be empty, otherwise non-empty; tail may or may not have changed.", currIndex);

            return true;
        }

        public static void OS_Iterate(StateImpl start, int k)
        {
#if ! __VISIBLE_ABSTRACTION__
            Console.WriteLine("OS_Iterate: Error: visible-state abstraction is disabled; aborting");
            Environment.Exit(-1);
#endif

            if (k == 0)
            {
                Console.WriteLine("OS Exploration: skipping k=0 (makes no sense)");
                OS_Iterate(start, 1);
            }

            Console.Write("About to explore state space for queue bound k = {0}. Press <ENTER> to continue, anything else to 'Exit(0)': ", k);
            if (!String.IsNullOrEmpty(Console.ReadLine()))
            {
                Console.WriteLine("Exiting.");
                Environment.Exit(0);
            }

            PrtEventBuffer.k = k;
            Dfs(start);

            if (size_Visited_previous == visited.Count)
            {
                Console.WriteLine("Global state sequence converged!");
                Console.Write("For fun, do you want to run the abstract convergence test as well? Press <ENTER> to continue, anything else to 'Exit(0)': ");
                if (!String.IsNullOrEmpty(Console.ReadLine()))
                {
                    Console.WriteLine("Exiting.");
                    Environment.Exit(0);
                }
            }

            // when do we have to run the abstract convergence test?
            if (size_Visible_previous_previous < size_Visible_previous && size_Visible_previous == visible.Count)
            {
                Console.WriteLine("New plateau detected.");
                Console.Write("Running abstract state convergence test with tail-list abstraction ... ");

                if (visible_converged())
                {
                    Console.WriteLine("converged!");
                    Environment.Exit(0);
                }
            }

            size_Visible_previous_previous = size_Visible_previous;
            size_Visible_previous = visible.Count;
            size_Visited_previous = visited.Count;

            OS_Iterate(start, k + 1);

        }

        static void PrintStackDepth(int depth)
        {
            for (int i = 0; i < depth; i++)
            {
                Console.Write(".");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// - runs the state machine pointed to by CurrIndex, in place, and returns the successor wrapped into a bstate. Nothing gets cloned.
        /// - assigns to argument a clone (!) of the old bstate, and advances its choice vector and currIndex, as appropriate
        /// So bstate points to new memory after calling Execute. The returned successor is stored in old memory.
        /// </summary>
        /// <param name="bstate"></param>
        /// <returns></returns>
        static BacktrackingState Execute(BacktrackingState bstate)
        {
            var origState = (StateImpl)bstate.State.Clone();

            int choiceIndex = 0;

            // bstate.State.UserBooleanChoice is a pointer to a function with signature f: {} -> Bool.
            // The following assigns the code under 'delegate' to this function pointer.
            // bstate and choiceIndex are global variables
            bstate.State.UserBooleanChoice = delegate ()
            {
                if (choiceIndex < bstate.ChoiceVector.Count)
                {
                    return bstate.ChoiceVector[choiceIndex++];
                }

                choiceIndex++;
                bstate.ChoiceVector.Add(false);
                return false;
            };

            bstate.State.EnabledMachines[bstate.CurrIndex].PrtRunStateMachine();

            Debug.Assert(choiceIndex == bstate.ChoiceVector.Count);

            // flip last choice          
            while (bstate.ChoiceVector.Count > 0 && bstate.ChoiceVector[bstate.ChoiceVector.Count - 1])
            {
                bstate.ChoiceVector.RemoveAt(bstate.ChoiceVector.Count - 1);
            }

            if (bstate.ChoiceVector.Count > 0)
            {
                bstate.ChoiceVector[bstate.ChoiceVector.Count - 1] = true;
            }

            var ret = new BacktrackingState(bstate.State);
            ret.depth = bstate.depth + 1;

            bstate.State = origState;

            if (bstate.ChoiceVector.Count == 0)
            {
                bstate.CurrIndex++; // first iterate through all choices. When exhausted, step to the next enabled machine
            }

            return ret;
        }

        static bool CheckFailure(StateImpl s, int depth)
        {
            if (UseDepthBounding && depth > DepthBound)
            {
                return true;
            }

            if (s.Exception == null)
            {
                return false;
            }


            if (s.Exception is PrtAssumeFailureException)
            {
                return true;
            }
            else if (s.Exception is PrtException)
            {
                Console.WriteLine(s.errorTrace.ToString());
                Console.WriteLine("ERROR: {0}", s.Exception.Message);
                Environment.Exit(-1);
            }
            else
            {
                Console.WriteLine(s.errorTrace.ToString());
                Console.WriteLine("[Internal Exception]: Please report to the P Team");
                Console.WriteLine(s.Exception.ToString());
                Environment.Exit(-1);
            }
            return false;
        }
    }

    class BacktrackingState
    {
        public StateImpl State;
        public int CurrIndex;            // index of the next machine to execute
        public List<bool> ChoiceVector;  // length = number of choices to be made; contents of list = current choice as bitvector
        public int depth;                // used only with depth bounding

        public BacktrackingState(StateImpl state)
        {
            this.State = state;
            CurrIndex = 0;
            ChoiceVector = new List<bool>();
            depth = 0;
        }

    }

    // Step I: define what an "abstract state" is. The general guidelines are as follows. An abstract state consists of two parts:
    // 1. the /visible fragment/ of the state, "visible state" for short, which is a part of the state information that is kept concretely, precisely, in plain text; and
    // 2. an abstraction of the rest of the state information (which may be implicit if the visible fragment is some sort of approximation).
    // This state partitioning should satisfy two properties:
    // (a) It defines a finite state space. That is, the set of visible fragments + abstractions of the rest of all conceivable states is finite.
    // (b) It contains "most" of the information needed to determine fireability of a transition and the visible fragment of the successor state.
    // (c) It contains enough information to decide whether some target safety property is satisfied.
    // As an example, for the common case of a message passing system with finitely many local states, a finite set of message + payload types, but unbounded queues,
    // the visible state might consist of
    // * the complete local state, plus
    // * the head of the queue of each machine, plus
    // * the /set/ of items in the tail of the queue.
    // The "abstraction of the rest" amounts to ignoring the multiplicity and ordering of messages in the tail of the queue.
    // This defines a finite state space and allows us to decide whether e.g. the system is responsive (we don't need the tail of the queue for that).
    // It is enough info to decide whether a transition is fireable: depends on local state and queue head.
    // Finally, abstract successors of RECEIVEs cannot be precisely computed since the head of the successor queue is unknown (but we know it is a member of the tail list).

    // This is implemented in the StateImpl::abstract_me function
    // Changing StateImpl is not very elegant: would be better to have a class VState derived from a StateImpl, that adds the abstraction capabilities

}
