<script setup lang="ts">
import { computed, ref } from 'vue'

function isValidChunk(value: number): boolean {
  return value >= 0 && value < 720896 && value % 11 === 0
}

function formatChunk(value: number): string {
  return value.toString().padStart(6, '0')
}

function randomValidChunk(): number {
  const maxMultiple = Math.floor((720896 - 1) / 11)
  const randomIndex = Math.floor(Math.random() * (maxMultiple + 1))
  return randomIndex * 11
}

const chunks = ref<string[]>(Array.from({ length: 8 }, () => formatChunk(randomValidChunk())))
const copied = ref(false)

const normalizedChunks = computed(() =>
  chunks.value.map((chunk) => chunk.replace(/\D/g, '').slice(0, 6).padStart(6, '0'))
)

const invalidIndexes = computed(() =>
  normalizedChunks.value.flatMap((chunk, index) => {
    const value = Number.parseInt(chunk, 10)
    return Number.isFinite(value) && isValidChunk(value) ? [] : [index]
  })
)

const recoveryPassword = computed(() => normalizedChunks.value.join('-'))
const isValid = computed(() => invalidIndexes.value.length === 0)

function regenerate() {
  chunks.value = Array.from({ length: 8 }, () => formatChunk(randomValidChunk()))
  copied.value = false
}

async function copyValue() {
  await navigator.clipboard.writeText(recoveryPassword.value)
  copied.value = true
  setTimeout(() => {
    copied.value = false
  }, 1500)
}
</script>

<template>
  <div class="recovery-generator">
    <div class="recovery-generator__header">
      <strong>BitLocker 恢复密钥生成器</strong>
      <span>8 组 × 6 位，每组可被 11 整除且小于 720896</span>
    </div>

    <div class="recovery-generator__grid">
      <label
        v-for="(chunk, index) in chunks"
        :key="index"
        class="recovery-generator__field"
      >
        <span>第 {{ index + 1 }} 组</span>
        <input
          v-model="chunks[index]"
          maxlength="6"
          inputmode="numeric"
          :class="{ 'recovery-generator__input--invalid': invalidIndexes.includes(index) }"
        />
      </label>
    </div>

    <div class="recovery-generator__result">
      <code>{{ recoveryPassword }}</code>
    </div>

    <div class="recovery-generator__actions">
      <button type="button" @click="regenerate">重新生成</button>
      <button type="button" @click="copyValue">复制结果</button>
      <span :class="isValid ? 'recovery-generator__ok' : 'recovery-generator__warn'">
        {{ isValid ? (copied ? '已复制' : '格式有效') : '存在不合法分组' }}
      </span>
    </div>
  </div>
</template>
