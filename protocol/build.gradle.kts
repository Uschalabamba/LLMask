import com.jetbrains.rd.generator.gradle.RdGenTask

plugins {
    id("org.jetbrains.kotlin.jvm")
    id("com.jetbrains.rdgen") version libs.versions.rdGen
}

dependencies {
    implementation(libs.kotlinStdLib)
    implementation(libs.rdGen)
    implementation(
        project(
            mapOf(
                "path" to ":",
                "configuration" to "riderModel"
            )
        )
    )
}

val DotnetPluginId: String by rootProject
val RiderPluginId: String by rootProject

rdgen {
    val csOutput = File(rootDir, "src/dotnet/${DotnetPluginId}")
    val ktOutput = File(rootDir, "src/rider/main/kotlin/com/jetbrains/rider/plugins/${RiderPluginId.replace('.','/').toLowerCase()}")

    verbose = true
    packages = "model.rider"

    generator {
        language = "kotlin"
        transform = "asis"
        root = "com.jetbrains.rider.model.nova.ide.IdeRoot"
        namespace = "com.jetbrains.rider.model"
        directory = "$ktOutput"
    }

    generator {
        language = "csharp"
        transform = "reversed"
        root = "com.jetbrains.rider.model.nova.ide.IdeRoot"
        namespace = "JetBrains.Rider.Model"
        directory = "$csOutput"
    }
}

tasks.withType<RdGenTask> {
    val classPath = sourceSets["main"].runtimeClasspath
    dependsOn(classPath)
    classpath(classPath)

    // rider-model.jar is compiled with Java 21; ensure rdgen runs on JDK 21+
    val riderJbr = listOf(
        "C:/Program Files/JetBrains/JetBrains Rider 2025.3.0.1/jbr/bin/java.exe",
        "C:/Program Files/JetBrains/JetBrains Rider 2025.3/jbr/bin/java.exe"
    ).map(::File).firstOrNull(File::exists)
    if (riderJbr != null) {
        executable(riderJbr.absolutePath)
    }
}
