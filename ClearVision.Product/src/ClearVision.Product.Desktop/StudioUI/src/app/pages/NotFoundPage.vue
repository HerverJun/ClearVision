<template>
  <section
    class="product-page-state"
    data-studio-page="not-found"
  >
    <p>404</p>
    <h1>未找到此页面</h1>
    <span>该地址不属于当前 StudioUI 产品路由。</span>
    <RouterLink to="/overview">
      返回概览
    </RouterLink>
  </section>
</template>

<style scoped>
.product-page-state { width: min(620px, 100%); margin: 12vh auto 0; text-align: center; }
.product-page-state p { margin: 0; color: var(--cv-color-brand-text); font-size: 42px; font-weight: var(--cv-font-weight-semibold); }
.product-page-state h1 { margin: var(--cv-space-2) 0; font-size: var(--cv-font-size-2xl); }
.product-page-state span { display: block; margin-bottom: var(--cv-space-4); color: var(--cv-text-secondary); }
</style>
