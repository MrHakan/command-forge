package editor.bridge.mixin;

import editor.bridge.Bridge;
import net.minecraft.world.level.BaseCommandBlock;
import net.minecraft.server.level.ServerLevel;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(BaseCommandBlock.class)
public abstract class BaseCommandBlockMixin {
    @Inject(method = "performCommand", at = @At("HEAD"), cancellable = true)
    private void editorExecutionGate(ServerLevel level, CallbackInfoReturnable<Boolean> ci) {
        if (!Bridge.mayExecute((BaseCommandBlock)(Object)this)) ci.setReturnValue(false);
    }
}
