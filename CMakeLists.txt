cmake_minimum_required(VERSION 3.20)
project(Launchpad NONE)

# ---------------------------------------------------------------------------
# CMake orchestrates the build; the .NET SDK still compiles the C# code.
# There is no C# compiler support in CMake itself, so every target below is a
# custom command that shells out to `dotnet`. This gives one command
# (`cmake --build build`) that works identically on Windows and Linux,
# replacing the old .sln.
# ---------------------------------------------------------------------------

set(LAUNCHPAD_CONFIGURATION "Release" CACHE STRING "Build configuration (Debug/Release)")

# Both projects publish as win-x64 .exe files.
# On Linux, run Launchpad.exe through Heroic / Wine the same way the game runs.
set(LAUNCHPAD_RID "win-x64")

set(LAUNCHPAD_OUT "${CMAKE_BINARY_DIR}/publish")

# Deliberately publish to the default bin/<config>/<tfm>/<rid>/publish
# location (no -o override): Launchpad.csproj's <None Include> glob for the
# Injector's files points at that exact path, so `dotnet publish` on the
# Launchpad project can pick up the already-published Injector output.
add_custom_target(injector ALL
    COMMAND dotnet publish "${CMAKE_SOURCE_DIR}/Injector/Injector.csproj"
            -c ${LAUNCHPAD_CONFIGURATION}
            -r ${LAUNCHPAD_RID}
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Publishing Launchpad.Injector (${LAUNCHPAD_RID})"
    VERBATIM
)

# --- Launchpad UI ------------------------------------------------------
add_custom_target(launchpad ALL
    COMMAND dotnet publish "${CMAKE_SOURCE_DIR}/Launchpad/Launchpad.csproj"
            -c ${LAUNCHPAD_CONFIGURATION}
            -r ${LAUNCHPAD_RID}
            -o "${LAUNCHPAD_OUT}"
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Publishing Launchpad UI (${LAUNCHPAD_RID})"
    VERBATIM
)
add_dependencies(launchpad injector)

# --- Tests ------------------------------------------------------------
if(EXISTS "${CMAKE_SOURCE_DIR}/Launchpad.Tests/Launchpad.Tests.csproj")
    add_custom_target(test_launchpad
        COMMAND dotnet test "${CMAKE_SOURCE_DIR}/Launchpad.Tests/Launchpad.Tests.csproj" -c ${LAUNCHPAD_CONFIGURATION}
        WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
        COMMENT "Running Launchpad.Tests"
        VERBATIM
    )
endif()
