cmake_minimum_required(VERSION 3.20)
project(Launchpad NONE)

# ---------------------------------------------------------------------------
# CMake orchestrates the build; the .NET SDK does the actual compilation.
# Run:  cmake -B build && cmake --build build
# Output: build/publish/Launchpad.exe  (win-x64, runs on Windows and via Wine)
# ---------------------------------------------------------------------------

set(LAUNCHPAD_CONFIGURATION "Release" CACHE STRING "Build configuration (Debug/Release)")

set(LAUNCHPAD_OUT "${CMAKE_BINARY_DIR}/publish")

add_custom_target(launchpad ALL
    COMMAND dotnet publish "${CMAKE_SOURCE_DIR}/Launchpad/Launchpad.csproj"
            -c ${LAUNCHPAD_CONFIGURATION}
            -r win-x64
            -o "${LAUNCHPAD_OUT}"
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Publishing Launchpad (win-x64)"
    VERBATIM
)

# --- Tests (optional) -------------------------------------------------------
if(EXISTS "${CMAKE_SOURCE_DIR}/Launchpad.Tests/Launchpad.Tests.csproj")
    add_custom_target(test_launchpad
        COMMAND dotnet test "${CMAKE_SOURCE_DIR}/Launchpad.Tests/Launchpad.Tests.csproj"
                -c ${LAUNCHPAD_CONFIGURATION}
        WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
        COMMENT "Running Launchpad.Tests"
        VERBATIM
    )
endif()
