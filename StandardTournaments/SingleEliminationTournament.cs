﻿//-----------------------------------------------------------------------
// <copyright file="SingleEliminationTournament.cs" company="(none)">
//  Copyright (c) 2009 John Gietzen
//
//  Permission is hereby granted, free of charge, to any person obtaining
//  a copy of this software and associated documentation files (the
//  "Software"), to deal in the Software without restriction, including
//  without limitation the rights to use, copy, modify, merge, publish,
//  distribute, sublicense, and/or sell copies of the Software, and to
//  permit persons to whom the Software is furnished to do so, subject to
//  the following conditions:
//
//  The above copyright notice and this permission notice shall be
//  included in all copies or substantial portions of the Software.
//
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//  EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//  MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//  NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
//  BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
//  ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
//  CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//  SOFTWARE
// </copyright>
// <author>John Gietzen</author>
//-----------------------------------------------------------------------

namespace Tournaments.Standard
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Xml;
    using System.Globalization;
    using Tournaments.Graphics;

    /// <summary>
    /// Implements a Singe Elmination Tournament
    /// </summary>
    public class SingleEliminationTournament : IPairingsGenerator, ITournamentVisualizer
    {
        private List<TournamentTeam> loadedTeams;
        private EliminationNode loadedRootNode;
        private PairingsGeneratorState state = PairingsGeneratorState.NotInitialized;

        public string Name
        {
            get
            {
                return "Single-elimination";
            }
        }

        public PairingsGeneratorState State
        {
            get
            {
                return this.state;
            }
        }

        public bool SupportsLateEntry
        {
            get
            {
                return false;
            }
        }

        public void Reset()
        {
            this.loadedTeams = null;
            this.loadedRootNode = null;
            this.state = PairingsGeneratorState.NotInitialized;
        }

        public void LoadState(IEnumerable<TournamentTeam> teams, IList<TournamentRound> rounds)
        {
            if (teams == null)
            {
                throw new ArgumentNullException("teams");
            }

            if (rounds == null)
            {
                throw new ArgumentNullException("rounds");
            }

            if (rounds.Where(r => r.Pairings.Where(p => p.TeamScores.Count > 2).Any()).Any())
            {
                throw new InvalidTournamentStateException("At least one pairing had more than two teams competing.  This is invalid in a single elimination tournament.");
            }

            var rootNode = BuildTree(teams);

            foreach (var round in rounds)
            {
                foreach (var pairing in round.Pairings)
                {
                    if (pairing.TeamScores.Count == 0)
                    {
                        continue;
                    }

                    bool success = rootNode.ApplyPairing(pairing);

                    if (!success)
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            this.loadedRootNode = rootNode;
            this.loadedTeams = new List<TournamentTeam>(teams);
            this.state = PairingsGeneratorState.Initialized;
        }

        private EliminationNode BuildTree(IEnumerable<TournamentTeam> teams)
        {
            List<EliminationNode> nodes = new List<EliminationNode>();

            if (teams.Count() >= 2)
            {
                int ranking = 0;
                var teamsOrder = from team in teams
                                 orderby team.Rating.HasValue ? team.Rating : 0 descending
                                 select new TeamRanking
                                 {
                                     Team = team,
                                     Ranking = ranking++,
                                 };

                int i = 0;
                int nextRoundAt = 2;
                int roundNumber = 0;
                int mask = (1 << roundNumber) - 1;
                var teamRankings = teamsOrder.ToList();
                foreach (var teamRanking in teamRankings)
                {
                    if (i == nextRoundAt)
                    {
                        nextRoundAt *= 2;
                        roundNumber += 1;
                        mask = (1 << roundNumber) - 1;
                    }

                    var newDecider = new TeamDecider(teamRanking.Team);
                    var newNode = new WinnerNode(newDecider);
                    newDecider.PrimaryParent = newNode;

                    if (nodes.Count > 0)
                    {
                        var match = (from n in nodes
                                     let d = n.Decider as TeamDecider
                                     where d != null
                                     where (teamRankings.Where(tr => tr.Team == n.Team).Single().Ranking & mask) == (teamRanking.Ranking & mask)
                                     select n).Single();

                        var newParent = MakeSiblings(match, newNode);
                        nodes.Add(newParent);
                    }

                    nodes.Add(newNode);
                    i++;
                }

                // Add in byes to even out the left side of the bracket.
                for (ranking = teamRankings.Count; ranking < nextRoundAt; ranking++)
                {
                    EliminationNode newNode = new WinnerNode(new ByeDecider());

                    var match = (from n in nodes
                                 let d = n.Decider as TeamDecider
                                 where d != null
                                 where (teamRankings.Where(tr => tr.Team == n.Team).Single().Ranking & mask) == (ranking & mask)
                                 select n).Single();

                    var newParent = MakeSiblings(match, newNode);
                    nodes.Add(newNode);
                    nodes.Add(newParent);
                }

            }

            return (from n in nodes
                    where n.Level == 0
                    select n).SingleOrDefault();
        }

        private bool ChildAMatches(ContinuationDecider decider, long teamId)
        {
            return decider != null && decider.ChildA != null && decider.ChildA.IsDecided && decider.ChildA.Team != null && decider.ChildA.Team.TeamId == teamId;
        }

        private bool ChildBMatches(ContinuationDecider decider, long teamId)
        {
            return decider != null && decider.ChildB != null && decider.ChildB.IsDecided && decider.ChildB.Team != null && decider.ChildB.Team.TeamId == teamId;
        }

        private EliminationNode MakeSiblings(EliminationNode nodeA, EliminationNode nodeB)
        {
            var oldParent = nodeA.PrimaryParent as ContinuationDecider;

            var newDecider = new ContinuationDecider(nodeA, nodeB);
            var newNode = new WinnerNode(newDecider);
            newDecider.PrimaryParent = newNode;
            nodeA.PrimaryParent = nodeB.PrimaryParent = newDecider;

            newNode.PrimaryParent = oldParent;
            if (oldParent != null)
            {
                if (oldParent.ChildA == nodeA)
                {
                    oldParent.ChildA = newNode;
                }
                else if (oldParent.ChildB == nodeA)
                {
                    oldParent.ChildB = newNode;
                }
            }
            
            return newNode;
        }

        private static void SwapChildrenAA(ContinuationDecider node1, ContinuationDecider node2)
        {
            var temp1 = node1.ChildA;
            node1.ChildA = null;
            var temp2 = node2.ChildA;
            node2.ChildA = null;

            node1.ChildA = temp2;
            node2.ChildA = temp1;
        }

        private static void SwapChildrenAB(ContinuationDecider node1, ContinuationDecider node2)
        {
            var temp1 = node1.ChildA;
            node1.ChildA = null;
            var temp2 = node2.ChildB;
            node2.ChildB = null;

            node1.ChildA = temp2;
            node2.ChildB = temp1;
        }

        private static void SwapChildrenBA(ContinuationDecider node1, ContinuationDecider node2)
        {
            var temp1 = node1.ChildB;
            node1.ChildB = null;
            var temp2 = node2.ChildA;
            node2.ChildA = null;

            node1.ChildB = temp2;
            node2.ChildA = temp1;
        }

        private static void SwapChildrenBB(ContinuationDecider node1, ContinuationDecider node2)
        {
            var temp1 = node1.ChildB;
            node1.ChildB = null;
            var temp2 = node2.ChildB;
            node2.ChildB = null;

            node1.ChildB = temp2;
            node2.ChildB = temp1;
        }

        private static void LockByes(List<EliminationNode> nodes)
        {
            var unlockedByes = from n in nodes
                               let d = n.Decider as ContinuationDecider
                               where d != null
                               //where n.Locked == false
                               where d.ChildB.Decider is ByeDecider || d.ChildA.Decider is ByeDecider
                               select n;

            foreach (var u in unlockedByes)
            {
                //u.Locked = true;
            }
        }

        public TournamentRound CreateNextRound(int? places)
        {
            if (places.HasValue && places.Value < 0)
            {
                throw new ArgumentException("You must specify a number of places that is greater than zero.", "places");
            }

            if (this.state != PairingsGeneratorState.Initialized)
            {
                throw new InvalidTournamentStateException("This generator was never successfully initialized with a valid tournament state.");
            }

            if (this.loadedRootNode == null)
            {
                return null;
            }

            var readyToPlay = this.loadedRootNode.FindUndecided();

            if (readyToPlay.Count() == 0)
            {
                // if this is because the root is locked, return null
                // otherwise, return an error because there is either a tie or an unfinished round.

                if (!this.loadedRootNode.IsDecided)
                {
                    throw new InvalidTournamentStateException("The tournament is not in a state that will allow a new round to be created, because there is at least one pairing in a previous round left to execute.");
                }

                return null;
            }

            if (places.HasValue)
            {
                return new TournamentRound(readyToPlay.Take(places.Value).ToList());
            }
            else
            {
                return new TournamentRound(readyToPlay.ToList());
            }
        }

        public IEnumerable<TournamentRanking> GenerateRankings()
        {
            if (this.loadedTeams.Count >= 2)
            {
                //var maxLevel = this.loadedNodes.Max(n => n.Level);

                //var ranks = from t in this.loadedTeams
                //            let node = FindTeamsHighestNode(t.TeamId)
                //            let finished = node.Locked && node.IsDecided
                //            let winner = node.IsDecided && node.Team != null && node.Team.TeamId == t.TeamId
                //            let round = maxLevel - node.Level
                //            let rank = node.Level + (winner ? 1 : 2)
                //            orderby rank
                //            select new TournamentRanking(t, rank, (finished ? (winner ? "Winner" : "Eliminated in heat " + round) : "Continuing"));

                //return ranks;

                yield break;
            }
            else
            {
                throw new InvalidTournamentStateException("The tournament is not in a state that allows ranking.");
            }
        }

        //private EliminationNode FindTeamsHighestNode(long teamId)
        //{
        //    return (from n in this.loadedNodes
        //            let d = n.Decider as ContinuationDecider
        //            where d != null
        //            where ChildAMatches(d, teamId) || ChildBMatches(d, teamId)
        //            orderby n.Level
        //            select n).FirstOrDefault();
        //}

        public SizeF Measure(IGraphics graphics, TournamentNameTable teamNames)
        {
            if (this.loadedRootNode == null)
            {
                return new Size(0, 0);
            }

            var textHeight = GetTextHeight(graphics);

            var size = this.loadedRootNode.Measure(graphics, teamNames, textHeight);

            return new SizeF(size.Width + 10, size.Height + 10);
        }

        public void Render(IGraphics graphics, TournamentNameTable teamNames)
        {
            if (this.loadedRootNode == null)
            {
                return;
            }

            var textHeight = GetTextHeight(graphics);

            this.loadedRootNode.Render(graphics, teamNames, 5, 5, textHeight);
        }

        private const float TextYOffset = 3.0f;
        private Font UserboxFont = new Font(FontFamily.GenericSansSerif, 10.0f);
        private const float MinTextHeight = 20.0f;

        private float GetTextHeight(IGraphics g)
        {
            return Math.Max(g.MeasureString("abfgijlpqyAIJQ170,`'\"", UserboxFont).Height + TextYOffset * 2, MinTextHeight);
        }
    }
}
