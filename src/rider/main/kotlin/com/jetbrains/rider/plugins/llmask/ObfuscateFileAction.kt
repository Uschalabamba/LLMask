package com.jetbrains.rider.plugins.llmask

import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.ide.CopyPasteManager
import com.jetbrains.rd.framework.impl.RpcTimeouts
import com.jetbrains.rd.ide.model.lLMaskModel
import com.jetbrains.rider.projectView.solution
import java.awt.datatransfer.StringSelection

class ObfuscateFileAction : AnAction() {
    private val log = Logger.getInstance(ObfuscateFileAction::class.java)

    override fun update(e: AnActionEvent) {
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE)
        val project = e.project

        // Basic file filter — must be a .cs file with a loaded project
        if (file == null || file.isDirectory || file.extension?.lowercase() != "cs" || project == null) {
            e.presentation.isEnabledAndVisible = false
            return
        }

        // Hide the action if PSI obfuscation is disabled in settings.
        // isPsiObfuscationEnabled is a reactive property pushed from LLMaskHost
        // whenever the setting changes.  valueOrNull is null while the model is
        // not yet bound (early startup); default to visible in that case.
        val enabled = project.solution.protocol
            ?.lLMaskModel
            ?.isPsiObfuscationEnabled
            ?.valueOrNull
            ?: true

        e.presentation.isEnabledAndVisible = enabled
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return
        val protocol = project.solution.protocol ?: return
        val model = protocol.lLMaskModel
        try {
            val obfuscated = model.obfuscateFile.sync(file.path, RpcTimeouts.default)
            if (obfuscated.isNotEmpty()) {
                CopyPasteManager.getInstance().setContents(StringSelection(obfuscated))
                log.info("LLMask: obfuscated ${file.name} (${obfuscated.length} chars), copied to clipboard")
            }
        } catch (ex: Exception) {
            log.error("LLMask: failed to obfuscate ${file.name}", ex)
        }
    }
}
