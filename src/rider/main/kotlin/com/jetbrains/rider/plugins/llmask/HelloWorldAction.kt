package com.jetbrains.rider.plugins.llmask

import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.ui.Messages

class HelloWorldAction : AnAction() {
    private val log = Logger.getInstance(HelloWorldAction::class.java)

    override fun actionPerformed(e: AnActionEvent) {
        log.info("LLMask HelloWorldAction executed from Kotlin")
        Messages.showInputDialog(
            e.project,
            "Response from C# backend:",
            "LLMask",
            Messages.getInformationIcon(),
            "Hello World",
            null
        )
    }
}
