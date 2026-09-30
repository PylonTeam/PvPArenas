// Retired Arrange items implementation; retained for reference only.
// using Microsoft.Xna.Framework;
// using Microsoft.Xna.Framework.Graphics;
// using System;
// using System.Collections.Generic;
// using Terraria;
// using Terraria.GameInput;
// using Terraria.ID;
//
// namespace PvPArenas._Deprecated.LoadoutSelector;
//
// /// <summary>
// /// Spring motion used by item icons while the loadout reorder UI is active.
// /// State belongs to the displayed slot and is swapped with the cursor state
// /// whenever an item is picked up or placed.
// /// </summary>
// internal static class LoadoutItemPhysics
// {
//     private sealed class PhysicsState
//     {
//         internal Vector2 Offset;
//         internal Vector2 Velocity;
//         internal float Scale = 1f;
//         internal float Shadow;
//         internal float Rotation;
//         internal float RotationVelocity;
//         internal bool WasScrolling;
//         internal ulong LastUpdate = ulong.MaxValue;
//     }
//
//     // Defaults from the supplied realistic-inventory physics.
//     private const float Stiffness = .35f;
//     private const float Damping = .75f;
//     private const float Weight = .22f;
//     private const float MaxHoverScale = 1.25f;
//     private const float ShadowOffset = 2f;
//     private const float ScrollStrength = .35f;
//
//     private const float WiggleAmplitude = .104f;
//     private const float WiggleSpeed = .09f;
//
//     private static readonly Dictionary<int, PhysicsState> SlotStates = [];
//     private static PhysicsState cursorState = new();
//     private static Vector2 lastMouseScreen;
//     private static bool cursorWasScrolling;
//
//     internal static void Reset()
//     {
//         SlotStates.Clear();
//         cursorState = new PhysicsState();
//         lastMouseScreen = Main.MouseScreen;
//         cursorWasScrolling = false;
//     }
//
//     /// <summary>
//     /// Exchanges the motion of a slot and the cursor at the same time that their
//     /// items are exchanged, preserving momentum through pickup, placement, and swaps.
//     /// </summary>
//     internal static void SwapWithCursor(int slotKey)
//     {
//         PhysicsState slotState = GetState(slotKey);
//         SlotStates[slotKey] = cursorState;
//         cursorState = slotState;
//         lastMouseScreen = Main.MouseScreen;
//         cursorWasScrolling = false;
//     }
//
//     internal static void DrawSlotItem(
//         Item item,
//         Vector2 position,
//         float scale,
//         float sizeLimit,
//         float opacity,
//         int slotKey,
//         bool hovered)
//     {
//         if (item == null || item.IsAir)
//             return;
//
//         PhysicsState state = GetState(slotKey);
//         UpdateSlot(state, slotKey, hovered);
//
//         DrawItem(
//             item,
//             position + state.Offset,
//             scale * state.Scale,
//             sizeLimit,
//             opacity,
//             state.Rotation,
//             state.Shadow);
//     }
//
//     internal static void DrawCursorItem(
//         Item item,
//         Vector2 position,
//         float scale,
//         float sizeLimit,
//         float opacity)
//     {
//         if (item == null || item.IsAir)
//             return;
//
//         UpdateCursor();
//
//         float inertiaTilt = cursorState.Offset.X * .042f;
//
//         DrawItem(
//             item,
//             position + cursorState.Offset,
//             scale * cursorState.Scale,
//             sizeLimit,
//             opacity,
//             cursorState.Rotation + inertiaTilt,
//             cursorState.Shadow);
//     }
//
//     private static PhysicsState GetState(int slotKey)
//     {
//         if (!SlotStates.TryGetValue(slotKey, out PhysicsState state))
//             SlotStates[slotKey] = state = new PhysicsState();
//
//         return state;
//     }
//
//     private static void UpdateSlot(
//         PhysicsState state,
//         int slotKey,
//         bool hovered)
//     {
//         if (state.LastUpdate == Main.GameUpdateCount)
//             return;
//
//         state.LastUpdate = Main.GameUpdateCount;
//         state.Scale = MathHelper.Lerp(
//             state.Scale,
//             hovered ? MaxHoverScale : 1f,
//             .15f);
//         state.Shadow = MathHelper.Lerp(
//             state.Shadow,
//             hovered ? .4f : 0f,
//             .16f);
//
//         float wiggleTarget = MathF.Sin(
//             Main.GameUpdateCount * WiggleSpeed
//             + slotKey * 1.37f)
//             * WiggleAmplitude;
//
//         state.RotationVelocity +=
//             (wiggleTarget - state.Rotation) * .148f;
//         state.RotationVelocity *= .82f;
//
//         int scrollDelta = PlayerInput.ScrollWheelDelta;
//         if (hovered && scrollDelta != 0 && !state.WasScrolling)
//         {
//             state.RotationVelocity +=
//                 Math.Sign(scrollDelta) * .31f * ScrollStrength;
//             state.WasScrolling = true;
//         }
//         else if (scrollDelta == 0)
//         {
//             state.WasScrolling = false;
//         }
//
//         state.Rotation += state.RotationVelocity;
//         state.Velocity -= state.Offset * Stiffness;
//         state.Velocity *= hovered ? Damping : Damping * .75f;
//         state.Offset += state.Velocity;
//
//         if (state.Offset.LengthSquared() < .0001f)
//             state.Offset = Vector2.Zero;
//     }
//
//     private static void UpdateCursor()
//     {
//         if (cursorState.LastUpdate == Main.GameUpdateCount)
//             return;
//
//         cursorState.LastUpdate = Main.GameUpdateCount;
//
//         Vector2 mouseScreen = Main.MouseScreen;
//         Vector2 delta = mouseScreen - lastMouseScreen;
//         lastMouseScreen = mouseScreen;
//
//         if (delta.LengthSquared() > 1600f)
//             delta = Vector2.Normalize(delta) * 40f;
//
//         cursorState.Velocity -= delta * Weight;
//
//         int scrollDelta = PlayerInput.ScrollWheelDelta;
//         if (scrollDelta != 0 && !cursorWasScrolling)
//         {
//             cursorState.RotationVelocity +=
//                 Math.Sign(scrollDelta) * .29f * ScrollStrength;
//             cursorWasScrolling = true;
//         }
//         else if (scrollDelta == 0)
//         {
//             cursorWasScrolling = false;
//         }
//
//         float wiggleTarget = MathF.Sin(
//             Main.GameUpdateCount * WiggleSpeed)
//             * WiggleAmplitude;
//
//         cursorState.RotationVelocity +=
//             (wiggleTarget - cursorState.Rotation) * .148f;
//         cursorState.RotationVelocity *= .82f;
//         cursorState.Rotation += cursorState.RotationVelocity;
//         cursorState.Velocity -= cursorState.Offset * Stiffness;
//         cursorState.Velocity *= Damping;
//         cursorState.Offset += cursorState.Velocity;
//         cursorState.Scale = MathHelper.Lerp(cursorState.Scale, 1f, .15f);
//         cursorState.Shadow = MathHelper.Lerp(cursorState.Shadow, .28f, .16f);
//     }
//
//     private static void DrawItem(
//         Item item,
//         Vector2 position,
//         float scale,
//         float sizeLimit,
//         float opacity,
//         float rotation,
//         float shadowAlpha)
//     {
//         Main.GetItemDrawFrame(
//             item.type,
//             out Texture2D texture,
//             out Rectangle frame);
//
//         float fitScale = Math.Min(
//             1f,
//             sizeLimit / Math.Max(frame.Width, frame.Height));
//         float finalScale = scale * fitScale;
//         Vector2 origin = frame.Size() / 2f;
//         Color baseColor = item.GetAlpha(Color.White);
//
//         if (shadowAlpha > .008f)
//         {
//             float offset = ShadowOffset * finalScale;
//             float finalShadowAlpha =
//                 shadowAlpha * (baseColor.A / 255f) * opacity;
//
//             Main.spriteBatch.Draw(
//                 texture,
//                 position + new Vector2(offset),
//                 frame,
//                 Color.Black * finalShadowAlpha,
//                 rotation,
//                 origin,
//                 finalScale,
//                 SpriteEffects.None,
//                 0f);
//         }
//
//         Main.spriteBatch.Draw(
//             texture,
//             position,
//             frame,
//             baseColor * opacity,
//             rotation,
//             origin,
//             finalScale,
//             SpriteEffects.None,
//             0f);
//
//         if (ItemID.Sets.AnimatesAsSoul[item.type] ||
//             item.color != Color.Transparent)
//         {
//             Main.spriteBatch.Draw(
//                 texture,
//                 position,
//                 frame,
//                 item.GetColor(Color.White) * opacity,
//                 rotation,
//                 origin,
//                 finalScale,
//                 SpriteEffects.None,
//                 0f);
//         }
//     }
// }
