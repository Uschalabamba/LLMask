@file:Suppress("EXPERIMENTAL_API_USAGE","EXPERIMENTAL_UNSIGNED_LITERALS","PackageDirectoryMismatch","UnusedImport","unused","LocalVariableName","CanBeVal","PropertyName","EnumEntryName","ClassName","ObjectPropertyName","UnnecessaryVariable","SpellCheckingInspection")
package com.jetbrains.rd.ide.model

import com.jetbrains.rd.framework.*
import com.jetbrains.rd.framework.base.*
import com.jetbrains.rd.framework.impl.*

import com.jetbrains.rd.util.lifetime.*
import com.jetbrains.rd.util.reactive.*
import com.jetbrains.rd.util.string.*
import com.jetbrains.rd.util.*
import kotlin.time.Duration
import kotlin.reflect.KClass
import kotlin.jvm.JvmStatic



/**
 * #### Generated from [LLMaskModel.kt:7]
 */
class LLMaskModel private constructor(
    private val _obfuscateFile: RdCall<String, String>,
    private val _isPsiObfuscationEnabled: RdOptionalProperty<Boolean>
) : RdExtBase() {
    //companion
    
    companion object : ISerializersOwner {
        
        override fun registerSerializersCore(serializers: ISerializers)  {
        }
        
        
        @JvmStatic
        @JvmName("internalCreateModel")
        @Deprecated("Use create instead", ReplaceWith("create(lifetime, protocol)"))
        internal fun createModel(lifetime: Lifetime, protocol: IProtocol): LLMaskModel  {
            @Suppress("DEPRECATION")
            return create(lifetime, protocol)
        }
        
        @JvmStatic
        @Deprecated("Use protocol.lLMaskModel or revise the extension scope instead", ReplaceWith("protocol.lLMaskModel"))
        fun create(lifetime: Lifetime, protocol: IProtocol): LLMaskModel  {
            IdeRoot.register(protocol.serializers)
            
            return LLMaskModel()
        }
        
        
        const val serializationHash = -8690633136334751269L
        
    }
    override val serializersOwner: ISerializersOwner get() = LLMaskModel
    override val serializationHash: Long get() = LLMaskModel.serializationHash
    
    //fields
    val obfuscateFile: IRdCall<String, String> get() = _obfuscateFile
    val isPsiObfuscationEnabled: IOptProperty<Boolean> get() = _isPsiObfuscationEnabled
    //methods
    //initializer
    init {
        _isPsiObfuscationEnabled.optimizeNested = true
    }
    
    init {
        bindableChildren.add("obfuscateFile" to _obfuscateFile)
        bindableChildren.add("isPsiObfuscationEnabled" to _isPsiObfuscationEnabled)
    }
    
    //secondary constructor
    private constructor(
    ) : this(
        RdCall<String, String>(FrameworkMarshallers.String, FrameworkMarshallers.String),
        RdOptionalProperty<Boolean>(FrameworkMarshallers.Bool)
    )
    
    //equals trait
    //hash code trait
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("LLMaskModel (")
        printer.indent {
            print("obfuscateFile = "); _obfuscateFile.print(printer); println()
            print("isPsiObfuscationEnabled = "); _isPsiObfuscationEnabled.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    override fun deepClone(): LLMaskModel   {
        return LLMaskModel(
            _obfuscateFile.deepClonePolymorphic(),
            _isPsiObfuscationEnabled.deepClonePolymorphic()
        )
    }
    //contexts
    //threading
    override val extThreading: ExtThreadingKind get() = ExtThreadingKind.Default
}
val IProtocol.lLMaskModel get() = getOrCreateExtension(LLMaskModel::class) { @Suppress("DEPRECATION") LLMaskModel.create(lifetime, this) }

