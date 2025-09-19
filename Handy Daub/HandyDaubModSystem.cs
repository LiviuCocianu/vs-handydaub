using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Handy_Daub
{
    public class HandyDaubModSystem : ModSystem
    {
        // Starting with 1.21.0, daub and wattle blocks require 6 daub + 2 more pieces to reach full state
        int DAUB_FOR_BLOCK = 6;
        int DAUB_FOR_FILLING = 1;

        public override void Start(ICoreAPI api)
        {

        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            // In versions lower than 1.21.0, daub and wattle blocks require 10 daub + 2 more pieces to reach full state
            if (GameVersion.IsLowerVersionThan(api.WorldManager.SaveGame.LastSavedGameVersion, "1.21.0-pre1"))
            {
                DAUB_FOR_BLOCK = 10;
            }

            /**
             * This will only trigger when a block is SUCCESSFULLY used (e.g. applying daub to wattle fences)
             * 
             * This delegate will only get called AFTER daub is subtracted from inventory, so we should take
             * that into account when calculating later on
             */
            api.Event.DidUseBlock += (IServerPlayer byPlayer, BlockSelection blockSel) =>
            {
                if (byPlayer.CurrentBlockSelection == null || byPlayer.CurrentBlockSelection.Block == null) return;

                BlockSelection sel = byPlayer.CurrentBlockSelection;
                string code = sel.Block.FirstCodePart();

                // We only want to capture block usages related to wattle and daub
                if (code == "wattle" || code == "daub")
                {
                    ItemSlot heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
                    ItemStack heldItemStack = heldSlot.Itemstack;

                    string heldItemCode = "";

                    // When it's not null => not all daub from hand got used
                    // When it's null => hand is empty after usage
                    if(heldSlot != null & heldItemStack != null)
                    {
                        heldItemCode = heldItemStack.Item.Code.ToString().Replace("game:", "");
                        if (!heldItemCode.StartsWith("daubraw")) return;
                    }

                    /**
                     * These variables are used to get around issue with InventoryManager#Find deleting daub stacks that are less
                     * than the amount required to make a daub block. We store the excess amount and variant
                     * then give it back to the player later.
                     */
                    int daubAccumulator = 0;
                    string lastDaubCode = "";

                    // Iterate through inventory. Returning false is equivalent to continuining the loop, and true for stopping it
                    byPlayer.InventoryManager.Find((slot) =>
                    {
                        // If player has enough daub in their hand to create or fill daub blocks, don't do anything
                        if (code == "wattle" && heldItemStack != null && heldItemStack.StackSize > DAUB_FOR_BLOCK) return false;
                        if (code == "daub" && heldItemStack != null && heldItemStack.StackSize > DAUB_FOR_FILLING) return false;

                        if (slot.Empty) return false;
                        if (slot.Itemstack.Item == null) return false;
                        if (slot.GetHashCode() == heldSlot.GetHashCode()) return false; // Skip the hotbar slot with daub in it

                        string invItemCode = slot.Itemstack.Item.Code.ToString().Replace("game:", "");

                        // If player holds daub, but it's not the same type as the found daub itemstack, skip
                        if (heldItemStack != null && invItemCode != heldItemCode) return false;
                        // If player doesn't hold anything and the found itemstack is not daub, skip
                        if (heldItemStack == null && !invItemCode.StartsWith("daubraw")) return false;

                        // Amount to add to the held itemstack
                        int amount = slot.StackSize;
                        // Account for held daub amount before subtracting from found daub itemstack
                        int diff = heldItemStack == null ? -1 : (heldItemStack.Item.MaxStackSize - heldSlot.StackSize);
                        // Store held amount before adding more for later
                        int prevHeldStackSize = heldItemStack == null ? 0 : heldItemStack.StackSize;

                        // Change base amount to account for held amount
                        if (diff > -1 && amount > diff) amount = (slot.StackSize >= diff ? diff : slot.StackSize);

                        if(heldItemStack == null)
                        {
                            // Save code of found itemstack for later, since it will become null
                            lastDaubCode = slot.Itemstack.Item.Code;
                            // Put everything from found itemstack into active/held hotbar slot
                            heldSlot.Itemstack = slot.TakeOut(amount);
                        } else
                        {
                            // Take only needed amount from found slot
                            slot.TakeOut(amount);
                            // .. then add it to the active slot
                            heldItemStack.StackSize += amount;
                        }

                        // Sync server-side changes we just did with the client-side so players can see the amount change
                        slot.MarkDirty();
                        heldSlot.MarkDirty();

                        // If we skipped over small amounts of daub stacks in previous iterations, give them all back
                        if (daubAccumulator > 0)
                        {
                            byPlayer.InventoryManager.TryGiveItemstack(new ItemStack(api.World.GetItem(lastDaubCode), daubAccumulator));
                            daubAccumulator = 0;
                        }

                        // Keep iterating if added amount is still not enough to create a daub block
                        if (heldItemStack != null && prevHeldStackSize <= DAUB_FOR_BLOCK) return false;

                        // For scenarios where players used DAUB_FOR_BLOCK amount of daub or less and ran out
                        // It got consumed just a couple of code lines ago, but we need to track it and give it back to avoid item loss
                        if (heldItemStack == null && amount <= DAUB_FOR_BLOCK)
                        {
                            daubAccumulator += amount;
                            return false;
                        }

                        return true;
                    });
                }
            };
        }

        public override void StartClientSide(ICoreClientAPI api)
        {

        }

    }
}
