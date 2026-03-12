package com.jetbrains.rider.plugins.llmask

import com.jetbrains.rider.settings.simple.SimpleOptionsPage

class LLMaskOptionsPage : SimpleOptionsPage("LLMask", PAGE_ID) {
    companion object {
        const val PAGE_ID = "LLMask"
    }

    override fun getId() = PAGE_ID
}
