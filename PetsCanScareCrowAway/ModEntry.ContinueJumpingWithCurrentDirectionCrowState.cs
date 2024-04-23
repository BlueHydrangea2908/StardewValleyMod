using System;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.VisualBasic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using PurrplingCore.Movement;
using StardewValley.BellsAndWhistles;
using StardewValley.Extensions;
using HarmonyLib;
using System.Reflection.Emit;
using static StardewValley.Minigames.CraneGame;
using Microsoft.Xna.Framework.Content;
using System.ComponentModel.Design;

namespace PetsCanScareCrowAway;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Public methods
    *********/
    /// <summary>The mod entry point, called after the mod is first loaded.</summary>
    /// <param name="helper">Provides simplified APIs for writing mods.</param>
    public override void Entry(IModHelper helper)
    {
        var petMovementControllers = new List<NpcMovementController>();
        var addNewPetMovementController = (Pet pet) =>
        {
            petMovementControllers.Add(new NpcMovementController(pet, new PathFinder(pet.currentLocation, pet, Game1.player)));
        };
        helper.Events.GameLoop.SaveLoaded += (o, e) =>
        {
            var pets = Utility.getAllCharacters().OfType<Pet>();
            foreach (var pet in pets)
            {
                addNewPetMovementController(pet);
            }
        };

        var chasingCrow = () =>
        {

        };
        helper.Events.GameLoop.UpdateTicked += (o, e) =>
        {
            if (Director.CrowLocations.Any())
            {
                foreach (var controller in petMovementControllers)
                {
                    if (!controller.IsFollowing)
                    {
                        var comparer = Comparer<Vector2>.Create(new Comparison<Vector2>((first, second) =>
                        {
                            const int SMALLER = -1;
                            const int EQUAL = 0;
                            const int BIGGER = 1;

                            var distanceBetweenFirstAndControllerPosition = (first - controller.follower.Position).Length();
                            var distanceBetweenSecondAndControllerPosition = (second - controller.follower.Position).Length();

                            if (distanceBetweenFirstAndControllerPosition == distanceBetweenSecondAndControllerPosition)
                            {
                                return EQUAL;
                            }
                            else
                            {
                                if (distanceBetweenFirstAndControllerPosition > distanceBetweenSecondAndControllerPosition)
                                {
                                    return BIGGER;
                                }
                                else
                                {
                                    return SMALLER;
                                }
                            }
                        }));
                        var target = Director.CrowLocations.Min(comparer);
                        target.X /= 64f;
                        target.Y /= 64f;
                        controller.Reset();
                        controller.Speed = 1f;
                        controller.AcquireTarget(target);
                        var moveToTarget = new EventHandler<UpdateTickedEventArgs>((o, e) => controller.Update(e));
                        helper.Events.GameLoop.UpdateTicked += moveToTarget;
                        controller.EndOfRouteReached += (o, e) => helper.Events.GameLoop.UpdateTicked -= moveToTarget;
                    }
                }
            }
        };

        var movePet = (string command, string[] args) =>
        {
            Monitor.Log("Try to move every pet to specified location.");
            var target = new Vector2(float.Parse(args[0]), float.Parse(args[1]));
            foreach (var controller in petMovementControllers)
            {
                controller.Reset();
                controller.AcquireTarget(target);
                controller.Speed = 6f;
                var moveToTarget = new EventHandler<UpdateTickedEventArgs>((o, e) => controller.Update(e));
                helper.Events.GameLoop.UpdateTicked += moveToTarget;
                controller.EndOfRouteReached += (o, e) => helper.Events.GameLoop.UpdateTicked -= moveToTarget;
            }
        };
        this.Helper.ConsoleCommands.Add("MovePet", "Move all pets to a location in map.", movePet);

        var spawnCrow = (string command, string[] args) =>
        {
            Monitor.Log("Spawn crow method is called.");
            if (args.Length >= 2)
            {
                float x = float.Parse(args[0]);
                float y = float.Parse(args[1]);
                var vector = new Vector2(x, y);
                Farm farm = Game1.getFarm();
                MethodInfo? spawnCrowMethod = farm.GetType().GetMethod("doSpawnCrow", BindingFlags.NonPublic | BindingFlags.Instance);
                if (spawnCrowMethod is null)
                {
                    Monitor.Log("Reflection fail to load spawn crow method.");
                }
                else
                {
                    spawnCrowMethod.Invoke(farm, new object[] { vector });
                }
            }
            else
            {
                Monitor.Log("Missing location.");
            }
        };
        this.Helper.ConsoleCommands.Add("SpawnCrow", "Spawn crow at a location in map.", spawnCrow);

        var changeCrowState = (string command, string[] args) =>
        {
            string state = args[0];
            CrowActor crow = MyPatches.LastCreatedCrow; // Update the crow reference here
            if (crow != null)
            {
                crow.CurrentState = MyPatches.CrowStateDictionary[state];
            }
            else
            {
                Monitor.Log("something wrong.");
            }
        };
        this.Helper.ConsoleCommands.Add("ChangeCrowState", "", changeCrowState);

        var harmony = new Harmony(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(Farm), "doSpawnCrow", new Type[] { typeof(Vector2) }),
            transpiler: new HarmonyMethod(typeof(MyPatches.DoSpawnCrowPatch), nameof(MyPatches.DoSpawnCrowPatch.Transpiler)),
            postfix: new HarmonyMethod(typeof(MyPatches.DoSpawnCrowPatch), nameof(MyPatches.DoSpawnCrowPatch.Postfix)));

    }

    public class Director
    {
        public static IList<Vector2> CrowLocations = new List<Vector2>();
    }

    public static class MyPatches
    {
        public static CrowActor LastCreatedCrow { get; private set; }
        public static IDictionary<string, CrowState> CrowStateDictionary = new Dictionary<string, CrowState>
        {
            ["flyingaway"] = new FlyingAwayCrowState(),
            ["eatingcrop"] = new EatingCropCrowState(),
            ["sleeping"] = new SleepingCrowState(),
            ["jumpingwithcurrentdirection"] = new JumpingWithCurrentDirectionCrowState(),
            ["jumpingaroundcurrentlocation"] = new JumpingAroundCurrentLocationCrowState()
        };
        public static ICrowFactory CrowFactory = new CrowActorFactory();

        [HarmonyPatch(typeof(Farm), "doSpawnCrow")]
        public static class DoSpawnCrowPatch
        {
            public static Crow CreateCrow(int x, int y)
            {
                return CrowFactory.Produce(x, y);
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (var instruction in instructions)
                {
                    // If the instruction is creating a new Crow object
                    if (instruction.opcode == OpCodes.Newobj && instruction.operand == typeof(Crow).GetConstructor(new[] { typeof(int), typeof(int) }))
                    {
                        //instruction.operand = typeof(CrowActor).GetConstructor(new[] { typeof(int), typeof(int) });
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = typeof(DoSpawnCrowPatch).GetMethod("CreateCrow");
                    }

                    yield return instruction;
                }
            }

            public static void Postfix(Farm __instance)
            {
                // Assuming critters is a field of OriginalClass
                var critters = __instance.GetType().GetField("critters", BindingFlags.Public | BindingFlags.Instance).GetValue(__instance) as List<Critter>;

                if (critters != null && critters.Count > 0)
                {
                    var lastCritter = critters[critters.Count - 1];

                    if (lastCritter is CrowActor modifiedCrow)
                    {
                        // Store a reference to the last created ModifiedCrow
                        LastCreatedCrow = modifiedCrow;
                    }
                }
            }
        }
    }


    public interface ICrowFactory
    {
        public Crow Produce(int x, int y);
    }

    public class CrowActorFactory : ICrowFactory
    {
        public Crow Produce(int tileX, int tileY)
        {
            var product = new CrowActor(tileX, tileY);
            Director.CrowLocations.Add(product.position);
            return product;
        }
    }

    public class CrowActor : Crow
    {
        public CrowActor(int tileX, int tileY) : base(tileX, tileY)
        {

            CurrentState = MyPatches.CrowStateDictionary["eatingcrop"];
        }

        private CrowState? _currentState;
        public CrowState CurrentState
        {
            get => _currentState;
            set
            {
                if (this.sprite.CurrentAnimation is not null)
                {
                    this.sprite.CurrentAnimation.Clear();
                }
                _currentState = value;
            }
        }

        public override bool update(GameTime time, GameLocation environment)
        {
            var tempReturn = () =>
            {
                sprite.animateOnce(time);
                if (gravityAffectedDY < 0f || yJumpOffset < 0f)
                {
                    yJumpOffset += gravityAffectedDY;
                    gravityAffectedDY += 0.25f;
                }

                if (position.X < -128f || position.Y < -128f || position.X > (float)environment.map.DisplayWidth || position.Y > (float)environment.map.DisplayHeight)
                {
                    return true;
                }

                return false;
            };

            var farmer = Game1.player;
            var minimumDistanceWithFarmer = 3f;
            var pets = Game1.getFarm().characters.OfType<Pet>();
            var minimumDistanceWithPet = 3f;
            if (CurrentState is not FlyingAwayCrowState)
            {
                // check if crow should fly away, if yes, change state to flying away
                var temp = (this.position / 64f - farmer.Position / 64f).Length();
                if ((farmer.currentLocation is Farm
                && (this.position / 64f - farmer.Position / 64f).Length() <= minimumDistanceWithFarmer))
                {
                    CurrentState = MyPatches.CrowStateDictionary["flyingaway"];
                    Director.CrowLocations.Remove(this.position);
                    return tempReturn();
                }

                foreach (var pet in pets)
                {
                    if (pet.currentLocation is Farm
                    && (this.position / 64f - pet.Position / 64f).Length() <= minimumDistanceWithPet)
                    {
                        CurrentState = MyPatches.CrowStateDictionary["flyingaway"];
                        Director.CrowLocations.Remove(this.position);
                        return tempReturn();
                    }
                }

                //choose next random state
                if (this.sprite.CurrentAnimation == null)
                {
                    // bug, crow rarely change its state to jumping with current direction state. Don't know why.
                    if (Game1.random.NextDouble() > 0.003)
                    {
                        if (Game1.random.NextBool())
                        {
                            CurrentState = MyPatches.CrowStateDictionary["jumpingwithcurrentdirection"];
                        }
                        else if (Game1.random.NextBool())
                        {
                            CurrentState = MyPatches.CrowStateDictionary["sleeping"];
                        }
                        else if (Game1.random.NextBool())
                        {
                            CurrentState = MyPatches.CrowStateDictionary["eatingcrop"];
                        }
                        else
                        {
                            CurrentState = MyPatches.CrowStateDictionary["jumpingaroundcurrentlocation"];
                        }
                    }
                    else
                    {
                        if (Game1.random.NextDouble() <= 0.008)
                        {
                            CurrentState = MyPatches.CrowStateDictionary["flyingaway"];
                        }
                        else
                        {
                            sprite.currentFrame = baseFrame;
                        }
                    }

                }

            }

            CurrentState.Handle(this, time, environment);

            return tempReturn();

        }
    }

    public abstract class CrowState
    {
        public abstract void Handle(Crow crow, GameTime time, GameLocation environment);
    }

    public class FlyingAwayCrowState : CrowState
    {
        public override void Handle(Crow crow, GameTime time, GameLocation environment)
        {
            if (crow.sprite.CurrentAnimation == null)
            {
                var farmer = Game1.player;

                AnimatedSprite.endOfAnimationBehavior playFlap = (Farmer who) =>
                {
                    if (Utility.isOnScreen(crow.position, 64))
                    {
                        Game1.playSound("batFlap");
                    }
                };

                if (Game1.random.NextDouble() < 0.85)
                {
                    Game1.playSound("crow");
                }

                if (farmer.Position.X > crow.position.X)
                {
                    crow.flip = false;
                }
                else
                {
                    crow.flip = true;
                }

                //make crow fly away
                crow.sprite.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
                {
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 6), 40),
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 7), 40),
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 8), 40),
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 9), 40),
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 10), 40, secondaryArm: false, crow.flip, playFlap),
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 7), 40),
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 9), 40),
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 8), 40),
                    new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 7), 40)
                });
            }
            crow.sprite.loop = true;

            if (!crow.flip)
            {
                crow.position.X -= 6f;
            }
            else
            {
                crow.position.X += 6f;
            }
            crow.yOffset -= 2f;
        }
    }

    public class EatingCropCrowState : CrowState
    {
        public override void Handle(Crow crow, GameTime time, GameLocation environment)
        {
            if (crow.sprite.CurrentAnimation == null)
            {
                AnimatedSprite.endOfAnimationBehavior playPeck = (who) =>
                {
                    if (Utility.isOnScreen(crow.position, 64))
                    {
                        Game1.playSound("shiny4");
                    }
                };

                List<FarmerSprite.AnimationFrame> list = new List<FarmerSprite.AnimationFrame>();
                list.Add(new FarmerSprite.AnimationFrame((short)crow.baseFrame, 480));
                list.Add(new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 1), 170, secondaryArm: false, crow.flip));
                list.Add(new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 2), 170, secondaryArm: false, crow.flip));
                int num = Game1.random.Next(1, 5);
                for (int i = 0; i < num; i++)
                {
                    list.Add(new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 3), 70));
                    list.Add(new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 4), 100, secondaryArm: false, crow.flip, playPeck));
                }

                list.Add(new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 3), 100));
                list.Add(new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 2), 70, secondaryArm: false, crow.flip));
                list.Add(new FarmerSprite.AnimationFrame((short)(crow.baseFrame + 1), 70, secondaryArm: false, crow.flip));
                list.Add(new FarmerSprite.AnimationFrame((short)crow.baseFrame, 500, secondaryArm: false, crow.flip));
                crow.sprite.loop = false;
                crow.sprite.setCurrentAnimation(list);
            }
        }
    }

    public class SleepingCrowState : CrowState
    {
        public override void Handle(Crow crow, GameTime time, GameLocation environment)
        {
            if (crow.sprite.CurrentAnimation == null)
            {
                var list = new List<FarmerSprite.AnimationFrame>();
                list.Add(new FarmerSprite.AnimationFrame((short)crow.baseFrame + 5, 1400, false, crow.flip));
                crow.sprite.loop = false;
                crow.sprite.setCurrentAnimation(list);
                //crow.sprite.currentFrame = crow.baseFrame + 5;
            }
        }
    }

    // currently crow won't move in x direction. Don't know why. Maybe missing something from the original code.
    public class JumpingWithCurrentDirectionCrowState : CrowState
    {
        public override void Handle(Crow crow, GameTime time, GameLocation environment)
        {
            if (Game1.random.NextDouble() < 0.008 && crow.sprite.CurrentAnimation == null && crow.yJumpOffset >= 0f)
            {
                crow.gravityAffectedDY = -4f;
            }
            else if (crow.sprite.CurrentAnimation == null)
            {
                crow.sprite.currentFrame = crow.baseFrame;
            }
        }
    }

    public class JumpingAroundCurrentLocationCrowState : CrowState
    {
        public override void Handle(Crow crow, GameTime time, GameLocation environment)
        {
            if (Game1.random.NextDouble() < 0.008 && crow.sprite.CurrentAnimation == null && crow.yJumpOffset >= 0f)
            {
                crow.flip = !crow.flip;
                crow.gravityAffectedDY = -4f;
            }
            else if (crow.sprite.CurrentAnimation == null)
            {
                crow.sprite.currentFrame = crow.baseFrame;
            }
        }
    }

    //public class CrowActor : Critter
    //{
    //    
    //    public override bool update(GameTime time, GameLocation environment)
    //    {
    //        Farmer farmer = Utility.isThereAFarmerWithinDistance(position / 64f, 4, environment);
    //        if (yJumpOffset < 0f && state != 1)
    //        {
    //            if (!flip && !environment.isCollidingPosition(getBoundingBox(-2, 0), Game1.viewport, isFarmer: false, 0, glider: false, null, pathfinding: false, projectile: false, ignoreCharacterRequirement: true))
    //            {
    //                position.X -= 2f;
    //            }
    //            else if (!environment.isCollidingPosition(getBoundingBox(2, 0), Game1.viewport, isFarmer: false, 0, glider: false, null, pathfinding: false, projectile: false, ignoreCharacterRequirement: true))
    //            {
    //                position.X += 2f;
    //            }
    //        }

    //        if (farmer != null && state != 1)
    //        {
    //            if (Game1.random.NextDouble() < 0.85)
    //            {
    //                Game1.playSound("crow");
    //            }

    //            state = 1;
    //            if (farmer.Position.X > position.X)
    //            {
    //                flip = false;
    //            }
    //            else
    //            {
    //                flip = true;
    //            }

    //            //make crow fly away
    //            sprite.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
    //            {
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 6), 40),
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 7), 40),
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 8), 40),
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 9), 40),
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 10), 40, secondaryArm: false, flip, playFlap),
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 7), 40),
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 9), 40),
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 8), 40),
    //                new FarmerSprite.AnimationFrame((short)(baseFrame + 7), 40)
    //            });
    //            sprite.loop = true;
    //        }

    //        switch (state)
    //        {
    //            case 0:
    //                // eat crop. Confirmed.
    //                if (sprite.CurrentAnimation == null)
    //                {
    //                    List<FarmerSprite.AnimationFrame> list = new List<FarmerSprite.AnimationFrame>();
    //                    list.Add(new FarmerSprite.AnimationFrame((short)baseFrame, 480));
    //                    list.Add(new FarmerSprite.AnimationFrame((short)(baseFrame + 1), 170, secondaryArm: false, flip));
    //                    list.Add(new FarmerSprite.AnimationFrame((short)(baseFrame + 2), 170, secondaryArm: false, flip));
    //                    int num = Game1.random.Next(1, 5);
    //                    for (int i = 0; i < num; i++)
    //                    {
    //                        list.Add(new FarmerSprite.AnimationFrame((short)(baseFrame + 3), 70));
    //                        list.Add(new FarmerSprite.AnimationFrame((short)(baseFrame + 4), 100, secondaryArm: false, flip, playPeck));
    //                    }

    //                    list.Add(new FarmerSprite.AnimationFrame((short)(baseFrame + 3), 100));
    //                    list.Add(new FarmerSprite.AnimationFrame((short)(baseFrame + 2), 70, secondaryArm: false, flip));
    //                    list.Add(new FarmerSprite.AnimationFrame((short)(baseFrame + 1), 70, secondaryArm: false, flip));
    //                    list.Add(new FarmerSprite.AnimationFrame((short)baseFrame, 500, secondaryArm: false, flip, donePecking));
    //                    sprite.loop = false;
    //                    sprite.setCurrentAnimation(list);
    //                }

    //                break;
    //            case 1:
    //                //move crow out of map
    //                if (!flip)
    //                {
    //                    position.X -= 6f;
    //                }
    //                else
    //                {
    //                    position.X += 6f;
    //                }

    //                yOffset -= 2f;
    //                break;
    //            case 2:
    //                // make crow sleep
    //                if (sprite.CurrentAnimation == null)
    //                {
    //                    sprite.currentFrame = baseFrame + 5;
    //                }

    //                if (Game1.random.NextDouble() < 0.003 && sprite.CurrentAnimation == null)
    //                {
    //                    state = 3;
    //                }

    //                break;
    //            case 3:
    //                if (Game1.random.NextDouble() < 0.008 && sprite.CurrentAnimation == null && yJumpOffset >= 0f)
    //                {
    //                }
    //                else if (sprite.CurrentAnimation == null)
    //                {
    //                    sprite.currentFrame = baseFrame;
    //                }

    //                break;
    //        }

    //        return base.update(time, environment);
    //    }
    //}

}