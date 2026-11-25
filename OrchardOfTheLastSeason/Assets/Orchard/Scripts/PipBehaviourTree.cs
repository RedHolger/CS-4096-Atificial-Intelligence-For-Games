using System;
using System.Collections.Generic;

namespace Orchard
{
    // pip uses a behaviour tree...
    // a behaviour tree is a tree of nodes that decides what pip does...
    // every node, when ticked, returns one of three states:
    //   success... this node's job is done
    //   failure... this node could not do its job
    //   running... still working, tick me again next frame
    public enum BTStatus //george
    {
        Success,
        Failure,
        Running
    }

    // base node... every node in the tree can be ticked... george
    public abstract class BTNode
    {
        public abstract BTStatus Tick();
    }

    // selector (composite node)... like an OR... // george
    // tries its children in order and stops at the first that does NOT fail
    // (so it picks the first branch that applies). fails only if all fail...
    public class Selector : BTNode
    {
        List<BTNode> kids;

        public Selector(List<BTNode> kids)
        {
            this.kids = kids;
        }

        public override BTStatus Tick()
        {
            for (int i = 0; i < kids.Count; i++)
            {
                BTStatus s = kids[i].Tick();
                if (s != BTStatus.Failure)
                {
                    return s;
                }
            }
            return BTStatus.Failure;
        }
    }

    // sequence (composite node)... like an AND...
    // runs its children in order, every one must succeed. stops at the first
    // that is not a success (e.g. a condition fails, or an action is still running)...
    public class Sequence : BTNode
    {
        List<BTNode> kids;

        public Sequence(List<BTNode> kids)
        {
            this.kids = kids;
        }

        public override BTStatus Tick()
        {
            for (int i = 0; i < kids.Count; i++)
            {
                BTStatus s = kids[i].Tick();
                if (s != BTStatus.Success)
                {
                    return s;
                }
            }
            return BTStatus.Success;
        }
    }

    // leaf nodes (the lecture's "leaf")... each one is game code that asks pip
    // to check one thing or do one thing...
    public class IsHurtNode : BTNode
    {
        Pip pip;
        public IsHurtNode(Pip p) { pip = p; }
        public override BTStatus Tick() { return pip.IsHurt(); }
    }

    public class RetreatNode : BTNode
    {
        Pip pip;
        public RetreatNode(Pip p) { pip = p; }
        public override BTStatus Tick() { return pip.RetreatToFarmer(); }
    }

    public class HasDamagedTreeNode : BTNode
    {
        Pip pip;
        public HasDamagedTreeNode(Pip p) { pip = p; }
        public override BTStatus Tick() { return pip.HasDamagedTree(); }
    }

    public class GoToTreeNode : BTNode
    {
        Pip pip;
        public GoToTreeNode(Pip p) { pip = p; }
        public override BTStatus Tick() { return pip.GoToTree(); }
    }

    public class RepairTreeNode : BTNode
    {
        Pip pip;
        public RepairTreeNode(Pip p) { pip = p; }
        public override BTStatus Tick() { return pip.RepairTree(); }
    }

    public class FollowNode : BTNode
    {
        Pip pip;
        public FollowNode(Pip p) { pip = p; }
        public override BTStatus Tick() { return pip.FollowFarmer(); }
    }

    // pip... behaviour tree 
    //
    // the tree, top branch first (selector picks the first branch that applies):
    //   sequence: am i hurt? -> run to the farmer...
    //   sequence: is a tree damaged? -> go to it -> repair it...
    //   else: follow the farmer...
    //
    // pip repairs the weakest tree. tie: the tree that was attacked first.
    // he finishes a repair unless another tree is much weaker (15%)
    public class Pip : Mover //george
    {
        public TreeEntity target;
        public int dir = 1;
        public double hp = 5;
        public double hurtFlash = 0;
        public bool retreating = false;
        public double dt = 0;

        BTNode root;
        TreeEntity found;
        bool following = false;

        public Pip(double x, double y)
        {
            this.x = x;
            this.y = y;
            r = 12;

            // build the tree...
            List<BTNode> seq1 = new List<BTNode>();
            seq1.Add(new IsHurtNode(this));
            seq1.Add(new RetreatNode(this));

            List<BTNode> seq2 = new List<BTNode>();
            seq2.Add(new HasDamagedTreeNode(this));
            seq2.Add(new GoToTreeNode(this));
            seq2.Add(new RepairTreeNode(this));

            List<BTNode> top = new List<BTNode>();
            top.Add(new Sequence(seq1));
            top.Add(new Sequence(seq2));
            top.Add(new FollowNode(this));
            root = new Selector(top);
        }

        public void Hurt(double d)
        {
            hp = Math.Max(0, hp - d);
            hurtFlash = 0.15;
        }

        public void update(double dt)
        {
            this.dt = dt;
            double px = x;
            if (hurtFlash > 0) hurtFlash = hurtFlash - dt;

            // slow healing...
            if (!retreating && hp < 5)
            {
                hp = Math.Min(5, hp + 0.15 * dt);
            }

            // tick the tree from the root (simple full traversal each frame)...
            root.Tick();

            if (x - px < -0.05) dir = -1;
            else if (x - px > 0.05) dir = 1;
        }

        // below 2.5 hp he runs away, back to normal at 4.5 hp...
        public BTStatus IsHurt()
        {
            if (!retreating && hp <= 2.5)
            {
                retreating = true;
                target = null;
                U.G.LogEvent("Pip is hurt, running to the farmer");
            }
            if (retreating && hp >= 4.5)
            {
                retreating = false;
            }
            if (retreating) return BTStatus.Success;
            return BTStatus.Failure;
        }

        public BTStatus RetreatToFarmer()
        {
            Steering.arrive(this, U.G.farmer, C.DOG_SPEED * 1.1, 30, dt, false);
            if (U.dist(this, U.G.farmer) < C.CELL * 1.3)
            {
                hp = Math.Min(5, hp + 1.2 * dt);
            }
            return BTStatus.Running;
        }

        public BTStatus HasDamagedTree()
        {
            found = weakestTree();
            if (found != null) return BTStatus.Success;
            return BTStatus.Failure;
        }

        public BTStatus GoToTree()
        {
            if (target != found)
            {
                target = found;
                U.G.LogEvent("Pip repairs the weakest tree");
            }
            if (U.dist(this, found) < 26) return BTStatus.Success;
            Steering.arrive(this, found, C.DOG_SPEED, 24, dt, false);
            return BTStatus.Running;
        }

        public BTStatus RepairTree()
        {
            found.heal(5, dt);
            vx = vx * 0.8;
            vy = vy * 0.8;
            if (found.hp >= found.maxhp)
            {
                target = null;
                return BTStatus.Success;
            }
            return BTStatus.Running;
        }

        public BTStatus FollowFarmer()
        {
            double d = U.dist(this, U.G.farmer);
            if (d > C.CELL * 1.6)
            {
                following = true;
            }
            else if (d < C.CELL * 1.0)
            {
                following = false;
            }
            if (following)
            {
                Steering.arrive(this, U.G.farmer, C.DOG_SPEED, 30, dt, false);
            }
            else
            {
                vx = vx * 0.85;
                vy = vy * 0.85;
            }
            return BTStatus.Success;
        }

        // which tree should pip repair... 
        TreeEntity weakestTree() //george
        {
            // lowest health fraction of the living trees...
            double minFrac = 1e9;
            bool any = false;
            for (int i = 0; i < U.G.trees.Count; i++)
            {
                TreeEntity t = U.G.trees[i];
                if (!t.alive) continue;
                any = true;
                double f = t.hp / t.maxhp;
                if (f < minFrac) minFrac = f;
            }
            if (!any) return null;

            // keep working on the current tree until it is full...
            bool busy = target != null && target.alive && target.hp < target.maxhp;
            if (minFrac > 0.985)
            {
                if (busy) return target;
                return null;
            }

            // among the weakest trees take the one attacked first...
            TreeEntity best = null;
            for (int i = 0; i < U.G.trees.Count; i++)
            {
                TreeEntity t = U.G.trees[i];
                if (!t.alive) continue;
                if (t.hp / t.maxhp <= minFrac + 0.01)
                {
                    if (best == null || t.firstHit < best.firstHit)
                    {
                        best = t;
                    }
                }
            }

            // unless another tree is much weaker...
            if (busy && best != target && best.hp / best.maxhp > target.hp / target.maxhp - 0.15)
            {
                return target;
            }
            return best;
        }
    }
}
