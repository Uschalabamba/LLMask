package model.rider

import com.jetbrains.rd.generator.nova.*
import com.jetbrains.rider.model.nova.ide.IdeRoot

@Suppress("unused")
object LLMaskModel : Ext(IdeRoot) {
    val obfuscateFile = call("obfuscateFile", PredefinedType.string, PredefinedType.string)
}
