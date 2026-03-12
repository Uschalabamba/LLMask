package model.rider

import com.jetbrains.rd.generator.nova.*
import com.jetbrains.rider.model.nova.ide.IdeRoot

@Suppress("unused")
object LLMaskModel : Ext(IdeRoot) {
    val obfuscateFile = call("obfuscateFile", PredefinedType.string, PredefinedType.string)

    // Reactive flag pushed from .NET whenever the UsePsiObfuscation setting changes.
    // The Kotlin side reads this in ObfuscateFileAction.update() to show/hide the action.
    val isPsiObfuscationEnabled = property("isPsiObfuscationEnabled", PredefinedType.bool)
}
