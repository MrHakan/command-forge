package editor.bridge.mixin;

import editor.bridge.Bridge;

import net.minecraft.client.server.IntegratedServer;
import net.minecraft.server.MinecraftServer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import java.util.function.BooleanSupplier;

@Mixin(IntegratedServer.class)
public abstract class IntegratedServerMixin {
    @Inject(method = "tickServer", at = @At("HEAD"))
    private void editorTick(BooleanSupplier time, CallbackInfo ci) {
        Bridge.tick((MinecraftServer)(Object)this);
    }
    @Inject(method = "stopServer", at = @At("HEAD"))
    private void editorStop(CallbackInfo ci) { Bridge.close((MinecraftServer)(Object)this); }
}
