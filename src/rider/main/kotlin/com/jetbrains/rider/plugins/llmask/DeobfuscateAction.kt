package com.jetbrains.rider.plugins.llmask

import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.ide.CopyPasteManager
import com.jetbrains.rd.framework.impl.RpcTimeouts
import com.jetbrains.rd.ide.model.lLMaskModel
import com.jetbrains.rider.projectView.solution
import java.awt.datatransfer.DataFlavor
import java.awt.datatransfer.StringSelection

class DeobfuscateAction : AnAction() {
    private val log = Logger.getInstance(DeobfuscateAction::class.java)

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val protocol = project.solution.protocol ?: return
        val model = protocol.lLMaskModel

        val clipboard = CopyPasteManager.getInstance()
        val text = clipboard.contents?.getTransferData(DataFlavor.stringFlavor) as? String
        if (text.isNullOrBlank()) {
            notify(project, "Clipboard is empty — nothing to deobfuscate.", NotificationType.WARNING)
            return
        }

        try {
            val deobfuscated = model.deobfuscateText.sync(text, RpcTimeouts.default)
            clipboard.setContents(StringSelection(deobfuscated))
            log.info("LLMask: deobfuscated ${text.length} → ${deobfuscated.length} chars, copied to clipboard")
            notify(project, "Deobfuscated text copied to clipboard.", NotificationType.INFORMATION)
        } catch (ex: Exception) {
            log.error("LLMask: deobfuscation failed", ex)
            notify(project, "Deobfuscation failed: ${ex.message}", NotificationType.ERROR)
        }
    }

    private fun notify(project: com.intellij.openapi.project.Project, message: String, type: NotificationType) {
        NotificationGroupManager.getInstance()
            .getNotificationGroup("LLMask")
            .createNotification(message, type)
            .notify(project)
    }
}
