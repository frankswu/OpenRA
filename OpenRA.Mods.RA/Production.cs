#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System.Drawing;
using System.Linq;
using OpenRA.FileFormats;
using OpenRA.Mods.RA;
using OpenRA.Mods.RA.Air;
using OpenRA.Mods.RA.Move;
using OpenRA.Traits;

namespace OpenRA.Mods.RA
{
	[Desc("This unit has access to build queues.")]
	public class ProductionInfo : ITraitInfo
	{
		[Desc("e.g. Infantry, Vehicles, Aircraft, Buildings")]
		public readonly string[] Produces = { };

		public virtual object Create(ActorInitializer init) { return new Production(this); }
	}

	[Desc("Where the unit should leave the building. Multiples are allowed if IDs are added: Exit@2, ...")]
	public class ExitInfo : TraitInfo<Exit>
	{
		public readonly int2 SpawnOffset = int2.Zero;	// in px relative to CenterLocation
		public readonly int2 ExitCell = int2.Zero;			// in cells relative to TopLeft
		public readonly int Facing = -1;

		public PVecInt SpawnOffsetVector { get { return (PVecInt)SpawnOffset; } }
		public CVec ExitCellVector { get { return (CVec)ExitCell; } }
	}
	public class Exit {}

	public class Production
	{
		public ProductionInfo Info;
		public Production(ProductionInfo info)
		{
			Info = info;
		}

		public void DoProduction(Actor self, ActorInfo producee, ExitInfo exitinfo)
		{
			var exit = self.Location + exitinfo.ExitCellVector;
			var spawn = self.Trait<IHasLocation>().PxPosition + exitinfo.SpawnOffsetVector;
			var to = Util.CenterOfCell(exit);

			var fi = producee.Traits.Get<IFacingInfo>();
			var initialFacing = exitinfo.Facing < 0 ? Util.GetFacing(to - spawn, fi.GetInitialFacing()) : exitinfo.Facing;

			var newUnit = self.World.CreateActor(producee.Name, new TypeDictionary
			{
				new OwnerInit(self.Owner),
				new LocationInit(exit),
				new FacingInit(initialFacing)
			});

			// TODO: Move this into an *Init
			// TODO: We should be adjusting the actual position for aircraft, not just visuals.
			var teleportable = newUnit.Trait<ITeleportable>();
			teleportable.AdjustPxPosition(newUnit, spawn);

			// TODO: Generalize this for non-mobile (e.g. aircraft) too
			// Remember to update the Enter activity too
			var mobile = newUnit.TraitOrDefault<Mobile>();
			if (mobile != null)
			{
				// Animate the spawn -> exit transition
				var speed = mobile.MovementSpeedForCell(newUnit, exit);
				var length = speed > 0 ? (int)((to - spawn).Length * 3 / speed) : 0;
				newUnit.QueueActivity(new Drag(spawn, to, length));
			}

			var target = MoveToRallyPoint(self, newUnit, exit);

			newUnit.SetTargetLine(Target.FromCell(target), Color.Green, false);
			foreach (var t in self.TraitsImplementing<INotifyProduction>())
				t.UnitProduced(self, newUnit, exit);
		}

		static CPos MoveToRallyPoint(Actor self, Actor newUnit, CPos exitLocation)
		{
			var rp = self.TraitOrDefault<RallyPoint>();
			if (rp == null)
				return exitLocation;

			var mobile = newUnit.TraitOrDefault<Mobile>();
			if (mobile != null)
			{
				newUnit.QueueActivity(new AttackMove.AttackMoveActivity(
					newUnit, mobile.MoveTo(rp.rallyPoint, rp.nearEnough)));
				return rp.rallyPoint;
			}

			// TODO: don't talk about HeliFly here.
			var helicopter = newUnit.TraitOrDefault<Helicopter>();
			if (helicopter != null)
			{
				newUnit.QueueActivity(new HeliFly(Util.CenterOfCell(rp.rallyPoint)));
				return rp.rallyPoint;
			}

			return exitLocation;
		}

		public virtual bool Produce(Actor self, ActorInfo producee)
		{
			if (Reservable.IsReserved(self))
				return false;

			// pick a spawn/exit point pair
			var exit = self.Info.Traits.WithInterface<ExitInfo>().Shuffle(self.World.SharedRandom)
				.FirstOrDefault(e => CanUseExit(self, producee, e));

			if (exit != null)
			{
				DoProduction(self, producee, exit);
				return true;
			}

			return false;
		}

		static bool CanUseExit(Actor self, ActorInfo producee, ExitInfo s)
		{
			var mobileInfo = producee.Traits.GetOrDefault<MobileInfo>();

			return mobileInfo == null ||
				mobileInfo.CanEnterCell(self.World, self, self.Location + s.ExitCellVector, self, true, true);
		}
	}
}
