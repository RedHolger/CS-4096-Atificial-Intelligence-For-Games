using System.Collections.Generic;

namespace Orchard
{
    // ---- tiny behaviour tree framework ----
    public enum BTStatus { Success, Failure, Running }

    public abstract class BTNode
    {
        public abstract BTStatus Tick();
    }

    // try children in order until one does not fail
    public class BTSelector : BTNode
    {
        BTNode[] kids;
        public BTSelector(BTNode[] kids) { this.kids = kids; }
        public override BTStatus Tick()
        {
            for (int i = 0; i < kids.Length; i++)
            {
                BTStatus s = kids[i].Tick();
                if (s != BTStatus.Failure) return s;
            }
            return BTStatus.Failure;
        }
    }

    // run children in order, stop at the first one that does not succeed
    public class BTSequence : BTNode
    {
        BTNode[] kids;
        public BTSequence(BTNode[] kids) { this.kids = kids; }
        public override BTStatus Tick()
        {
            for (int i = 0; i < kids.Length; i++)
            {
                BTStatus s = kids[i].Tick();
                if (s != BTStatus.Success) return s;
            }
            return BTStatus.Success;
        }
    }

    public class BTAction : BTNode
    {
        System.Func<BTStatus> fn;
        public BTAction(System.Func<BTStatus> fn) { this.fn = fn; }
        public override BTStatus Tick() { return fn(); }
    }

    // ai technique: behaviour tree
    // pip repairs the weakest living tree by standing next to it.
    // tie between equally weak trees -> the one that was attacked first.
    // once he starts a repair he finishes it (commitment), unless another tree becomes
    // clearly weaker (15% lower) - then he switches. if every tree is full he stays by the farmer.
    //
    //   selector
    //     sequence: [ hurt by a shambler pack? ] -> [ run to the farmer and recover ]   (new: top priority)
    //     sequence: [ has damaged tree? ] -> [ go to it (arrive) ] -> [ repair it ]
    //     action:   follow farmer (start beyond 1.6 cells, stop inside 1 cell)
    public class Pip : Mover
    {
        public TreeEntity target;
        public int dir = 1;
        public double hp = 5, hurtFlash;
        public bool retreating;

        public void Hurt(double d) { hp = System.Math.Max(0, hp - d); hurtFlash = 0.15; }

        BTNode root;
        double dt;
        TreeEntity found;
        bool following;

        public Pip(double x, double y)
        {
            this.x = x; this.y = y; r = 12;
            root = new BTSelector(new BTNode[] {
                new BTSequence(new BTNode[] {
                    new BTAction(IsHurt),
                    new BTAction(RetreatToFarmer)
                }),
                new BTSequence(new BTNode[] {
                    new BTAction(HasDamagedTree),
                    new BTAction(GoToTree),
                    new BTAction(RepairTree)
                }),
                new BTAction(FollowFarmer)
            });
        }

        public void update(double dt)
        {
            this.dt = dt;
            double px = x;
            if (hurtFlash > 0) hurtFlash -= dt;
            if (!retreating && hp < 5) hp = System.Math.Min(5, hp + 0.15 * dt);   // slow natural recovery
            root.Tick();
            if (x - px < -0.05) dir = -1;
            else if (x - px > 0.05) dir = 1;
        }

        // hurt: below 2.5 hp starts a retreat, which lasts until healed back to 4.5 (hysteresis)
        BTStatus IsHurt()
        {
            if (!retreating && hp <= 2.5)
            {
                retreating = true;
                target = null;
                U.G.LogEvent("Pip is hurt, running to the farmer");
            }
            if (retreating && hp >= 4.5) retreating = false;
            return retreating ? BTStatus.Success : BTStatus.Failure;
        }

        BTStatus RetreatToFarmer()
        {
            Steering.arrive(this, U.G.farmer, C.DOG_SPEED * 1.1, 30, dt, Steering.moveFree);
            if (U.dist(this, U.G.farmer) < C.CELL * 1.3) hp = System.Math.Min(5, hp + 1.2 * dt);
            return BTStatus.Running;
        }

        BTStatus HasDamagedTree()
        {
            found = weakestTree();
            return found != null ? BTStatus.Success : BTStatus.Failure;
        }

        BTStatus GoToTree()
        {
            if (target != found)
            {
                target = found;
                U.G.LogEvent("Pip repairs the weakest tree");
            }
            if (U.dist(this, found) < 26) return BTStatus.Success;
            Steering.arrive(this, found, C.DOG_SPEED, 24, dt, Steering.moveFree);
            return BTStatus.Running;
        }

        BTStatus RepairTree()
        {
            found.heal(5, dt);
            vx *= 0.8; vy *= 0.8;
            if (found.hp >= found.maxhp) { target = null; return BTStatus.Success; }   // job done
            return BTStatus.Running;
        }

        BTStatus FollowFarmer()
        {
            double d = U.dist(this, U.G.farmer);
            if (d > C.CELL * 1.6) following = true;
            else if (d < C.CELL * 1.0) following = false;
            if (following) Steering.arrive(this, U.G.farmer, C.DOG_SPEED, 30, dt, Steering.moveFree);
            else { vx *= 0.85; vy *= 0.85; }
            return BTStatus.Success;
        }

        public TreeEntity weakestTree()
        {
            List<TreeEntity> alive = new List<TreeEntity>();
            foreach (TreeEntity t in U.G.trees) if (t.alive) alive.Add(t);
            if (alive.Count == 0) return null;

            double minFrac = 1e9;
            foreach (TreeEntity t in alive)
            {
                double f = t.hp / t.maxhp;
                if (f < minFrac) minFrac = f;
            }

            // keep working on the current tree until it is full...
            bool busy = target != null && target.alive && target.hp < target.maxhp;
            if (minFrac > 0.985) return busy ? target : null; // nothing else needs repair

            // among the weakest, pick the tree that was attacked first (earliest firstHit)
            List<TreeEntity> cand = new List<TreeEntity>();
            foreach (TreeEntity t in alive) if (t.hp / t.maxhp <= minFrac + 0.01) cand.Add(t);
            U.StableSort(cand, (a, b) => a.firstHit.CompareTo(b.firstHit));
            TreeEntity best = cand[0];

            // ...unless another tree is clearly weaker
            if (busy && best != target && best.hp / best.maxhp > target.hp / target.maxhp - 0.15) return target;
            return best;
        }
    }
}
